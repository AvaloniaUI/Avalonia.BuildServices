using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace Avalonia.Telemetry;

// What kind of machine the build runs on, as two confidences from 0 to 100:
// - VmConfidence: 0 physical hardware, 100 a virtual machine.
// - ServerConfidence: 0 someone's desktop or laptop, 100 a server or cloud machine.
// The two are independent: WSL2, Docker Desktop and Parallels are virtual
// machines on someone's desk. Each probe reads a fixed fact about the hardware,
// firmware or operating system, so one machine always gets the same answer.
// Only the two numbers leave this class: never the strings or devices it read. 
//
// We should NEVER send raw device information to our telemetry system. 
internal static class MachineKindHelper
{
    // Nothing could be read, or nothing said either way.
    private const int NoEvidence = 50;

    // Finding something by name (a hypervisor, a cloud, a battery) is close to
    // certain. Finding its absence, or a hint, is less so: a hypervisor we do
    // not know looks like real hardware.
    private const int Strong = 45;
    private const int Weak = 25;
    
    private static readonly string[] HypervisorPrefixes =
    {
        "KVM", "OpenStack", "KubeVirt", "Alibaba Cloud ECS", "Amazon EC2", "QEMU", "VMware", "VMW", "innotek GmbH",
        "VirtualBox", "Xen", "Bochs", "Parallels", "BHYVE", "Hyper-V", "Apple Virtualization",
    };

    private const string GooglePrefix = "Google Compute Engine";

    // Clouds that name themselves in SMBIOS. Azure is told by its asset tag.
    // A cloud name says server. Whether it is a VM is for the hypervisor list
    // and the CPU flag to say: clouds sell bare metal under the same names.
    private static readonly string[] CloudPrefixes =
    {
        "Amazon EC2", "Google Compute Engine", "OpenStack", "KubeVirt", "Alibaba Cloud", "DigitalOcean", "Hetzner",
    };

    // Hypervisors sold for desktops: Parallels Desktop and VirtualBox.
    private static readonly string[] DesktopHypervisorPrefixes = { "Parallels", "innotek GmbH", "VirtualBox" };

    // Every Azure VM has this chassis asset tag; cloud-init detects Azure by it.
    private const string AzureAssetTag = "7783-7084-3265-9085-8269-3286-77";

    public static (byte VmConfidence, byte ServerConfidence) Detect()
    {
        var vm = new Score();
        var server = new Score();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            DetectWindows(vm, server);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            DetectMac(vm, server);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            DetectLinux(vm, server);

        return (vm.Value, server.Value);
    }

    // A positive weight is evidence for a VM (or a server), a negative one against.
    private sealed class Score
    {
        private int _value = NoEvidence;

        public void Add(int weight) => _value += weight;

        public byte Value => (byte)Math.Max(0, Math.Min(100, _value));
    }

    private static void Probe(Action probe)
    {
        try
        {
            probe();
        }
        catch
        {
            // An unreadable fact is no evidence.
        }
    }

    // SMBIOS identity: whether it names a hypervisor, or null when there was
    // nothing to read. Hyper-V and Azure name the vendor Microsoft Corporation,
    // as Surface laptops do, so they are told apart by the product. AWS
    // bare-metal instances are physical servers that still say Amazon EC2;
    // their product (the instance type) says ".metal". Google Compute Engine
    // is a VM when the CPU says so, or when the CPU cannot say (Arm, Windows).
    // An unrecognised machine in a known cloud is neither evidence for a VM
    // nor against: the cloud's own hypervisor may not name itself.
    private static bool? Firmware(Score vm, Score server, bool? cpuHypervisor, string? vendor, string? product, string? version, string? boardVendor, string? biosVendor)
    {
        var fields = new[] { vendor, product, version, boardVendor, biosVendor };
        if (fields.All(string.IsNullOrWhiteSpace))
            return null;

        var metal = StartsWith(vendor, "Amazon EC2") && IsMetalInstanceType(product);
        var cloud = StartsWithAny(fields, CloudPrefixes);
        var hypervisor = !metal && (fields.Any(f => StartsWith(f, GooglePrefix))
            ? cpuHypervisor != false
            : IsHyperV(vendor, product) || StartsWithAny(fields, HypervisorPrefixes));
        vm.Add(hypervisor ? Strong : cloud ? 0 : -Weak);

        if (cloud)
            server.Add(Strong);
        else if (StartsWithAny(fields, DesktopHypervisorPrefixes))
            server.Add(-Weak);

        return hypervisor;
    }

    private static bool IsHyperV(string? vendor, string? product) =>
        StartsWith(vendor, "Microsoft Corporation") && StartsWith(product, "Virtual Machine");

    // AWS instance types with ".metal" alone ("c5.metal") or before a size
    // ("m7i.metal-48xl") are bare metal; systemd matches the same two shapes.
    private static bool IsMetalInstanceType(string? product)
    {
        var type = product?.Trim() ?? "";
        var at = type.IndexOf(".metal", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return false;

        var end = at + ".metal".Length;
        return end == type.Length || type[end] == '-';
    }

    private static bool StartsWithAny(string?[] fields, string[] prefixes) =>
        fields.Any(f => prefixes.Any(p => StartsWith(f, p)));

    private static bool StartsWith(string? value, string prefix) =>
        value != null && value.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    // The CPU's hypervisor bit cannot help on Windows: with virtualisation-based
    // security on, physical Windows runs under Hyper-V too and the bit is set.
    private static void DetectWindows(Score vm, Score server)
    {
        // Windows copies the SMBIOS strings here at boot; any user can read them.
        Probe(() =>
        {
            const string bios = @"HARDWARE\DESCRIPTION\System\BIOS";
            Firmware(vm, server, null,
                ReadRegistryString(bios, "SystemManufacturer"),
                ReadRegistryString(bios, "SystemProductName"),
                ReadRegistryString(bios, "SystemVersion"),
                ReadRegistryString(bios, "BaseBoardManufacturer"),
                ReadRegistryString(bios, "BIOSVendor"));
        });

        // "Server", "Server Core" or "Nano Server" for Windows Server, which is
        // what hosted CI agents run; "Client" for Windows 10 and 11.
        Probe(() =>
        {
            var installationType = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallationType");
            if (installationType?.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
                server.Add(Strong);
            else if (string.Equals(installationType, "Client", StringComparison.OrdinalIgnoreCase))
                server.Add(-Weak);
        });

        // 128 is "no system battery", 255 "unknown". A VM on a laptop can see
        // the laptop's battery: Parallels passes it through.
        Probe(() =>
        {
            if (PInvoke.GetSystemPowerStatus(out var power) && power.BatteryFlag != 128 && power.BatteryFlag != 255)
                server.Add(-Strong);
        });
    }

    // The 64-bit view: Visual Studio 2019's MSBuild is a 32-bit process, and
    // HKLM\SOFTWARE would otherwise be redirected to WOW6432Node.
    private static unsafe string? ReadRegistryString(string subKey, string name)
    {
        const int capacity = 256;
        var buffer = stackalloc char[capacity];
        var size = (uint)(capacity * sizeof(char));
        fixed (char* subKeyPtr = subKey)
        fixed (char* namePtr = name)
        {
            var flags = REG_ROUTINE_FLAGS.RRF_RT_REG_SZ | REG_ROUTINE_FLAGS.RRF_SUBKEY_WOW6464KEY;
            if (PInvoke.RegGetValue(HKEY.HKEY_LOCAL_MACHINE, subKeyPtr, namePtr, flags, null, buffer, &size) != WIN32_ERROR.ERROR_SUCCESS)
                return null;
        }

        return new string(buffer, 0, (int)(size / sizeof(char))).TrimEnd('\0');
    }

    private static void DetectMac(Score vm, Score server)
    {
        // The kernel's own answer to "am I running under a hypervisor".
        Probe(() => vm.Add(MacUtil.ReadInt32("kern.hv_vmm_present") switch
        {
            1 => Strong,
            0 => -Weak,
            _ => 0,
        }));

        // Apple's Virtualization framework (Parallels and UTM use it for macOS
        // on Apple silicon) reports VirtualMacN,N; VMware Fusion reports
        // VMwareN,N. A real Mac reports its model, such as Mac14,5: desktop
        // hardware, though Mac minis also fill CI racks.
        Probe(() =>
        {
            var model = MacUtil.ReadString("hw.model");
            if (string.IsNullOrEmpty(model))
                return;

            if (StartsWith(model, "VirtualMac") || StartsWith(model, "VMware"))
            {
                vm.Add(Strong);
            }
            else
            {
                vm.Add(-Weak);
                server.Add(-Weak);
            }
        });

        // Only MacBooks have one.
        Probe(() =>
        {
            if (MacUtil.HasService("AppleSmartBattery"))
                server.Add(-Strong);
        });
    }

    private static void DetectLinux(Score vm, Score server)
    {
        // WSL runs on a Windows PC (LinuxHostHelper.IsWslKernel knows its
        // kernel names). LinuxKit ("…-linuxkit") is the VM Docker Desktop runs
        // containers in on Mac and Windows.
        Probe(() =>
        {
            var release = File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
            if (LinuxHostHelper.IsWslKernel(release) || release.EndsWith("-linuxkit", StringComparison.OrdinalIgnoreCase))
                server.Add(-Strong);
        });

        // x86 CPUs report "hypervisor" when a hypervisor runs them. Arm has no
        // such flag, and its /proc/cpuinfo has no "flags" line. Read before DMI,
        // which needs the answer for Google Compute Engine.
        bool? cpuHypervisor = null;
        Probe(() =>
        {
            var flags = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("flags", StringComparison.Ordinal));
            if (flags != null)
            {
                cpuHypervisor = flags.Split(' ', '\t').Contains("hypervisor");
                vm.Add(cpuHypervisor == true ? Strong : -Weak);
            }
        });

        // DMI is the kernel's copy of SMBIOS. Containers see the host's.
        Probe(() =>
        {
            var vendor = ReadDmi("sys_vendor");
            var product = ReadDmi("product_name");
            var hypervisor = Firmware(vm, server, cpuHypervisor, vendor, product, ReadDmi("product_version"), ReadDmi("board_vendor"), ReadDmi("bios_vendor"));

            // The asset tag is read only from a Hyper-V VM, where it is either
            // Azure's or one Hyper-V made up: never a company's own tag.
            if (hypervisor == true && IsHyperV(vendor, product) && ReadDmi("chassis_asset_tag")?.Trim() == AzureAssetTag)
                server.Add(Strong);

            // A hypervisor picks its VMs' chassis type (Hyper-V says Desktop),
            // so only physical hardware's counts.
            if (hypervisor == false && int.TryParse(ReadDmi("chassis_type")?.Trim(), out var chassis))
                server.Add(ChassisWeight(chassis));
        });

        // The SMBIOS BIOS Characteristics Extension Byte 2 (DSP0134 section
        // 7.1.2.2) has a "virtual machine" bit, exposed as byte 19 of the type 0
        // table. Root can read it. Set, it is a VM a hypervisor we may not know
        // of; clear says nothing, since QEMU with SeaBIOS leaves it clear.
        Probe(() =>
        {
            var raw = File.ReadAllBytes("/sys/firmware/dmi/entries/0-0/raw");
            if (raw.Length >= 20 && raw[1] >= 20 && (raw[19] & 0x10) != 0)
                vm.Add(Strong);
        });

        // Arm, POWER and RISC-V machines describe themselves in a device tree
        // instead of a CPU flag. A hypervisor node names the hypervisor
        // (linux,kvm, xen, vmware). QEMU's generic machines are "linux,dummy-virt"
        // (Arm) and "qemu,pseries" (POWER), and QEMU's fw-cfg device appears
        // nowhere else. A physical board has none of these, and x86 has no tree.
        Probe(() =>
        {
            const string tree = "/proc/device-tree";
            if (!Directory.Exists(tree))
                return;

            if (File.Exists(Path.Combine(tree, "hypervisor", "compatible"))
                || ReadStrings(Path.Combine(tree, "compatible")).Any(c => c is "linux,dummy-virt" or "qemu,pseries")
                || Directory.EnumerateFileSystemEntries(tree).Any(e => Path.GetFileName(e).Contains("fw-cfg")))
            {
                vm.Add(Strong);
            }
        });

        // Virtio is the device standard KVM, QEMU and Apple's Virtualization
        // framework give their guests; physical machines have none. It is what
        // gives away Docker Desktop's VM on Apple silicon, which has no DMI and
        // no CPU flag.
        Probe(() =>
        {
            const string devices = "/sys/bus/virtio/devices";
            if (Directory.Exists(devices) && Directory.EnumerateFileSystemEntries(devices).Any())
                vm.Add(Strong);
        });

        // A system battery: a laptop, or a VM on one. Wireless mice and headsets
        // report batteries too, with scope "Device".
        Probe(() =>
        {
            const string supplies = "/sys/class/power_supply";
            if (Directory.Exists(supplies) && Directory.EnumerateFileSystemEntries(supplies).Any(s =>
                    ReadText(Path.Combine(s, "type")) == "Battery" && ReadText(Path.Combine(s, "scope")) != "Device"))
            {
                server.Add(-Strong);
            }
        });
    }

    // SMBIOS chassis types, grouped as systemd's hostnamectl groups them
    // (src/hostname/hostnamed.c), plus rack mount and multi-system chassis.
    private static int ChassisWeight(int chassis) => chassis switch
    {
        // Portable, laptop, notebook, sub notebook, tablet, convertible, detachable.
        8 or 9 or 10 or 14 or 30 or 31 or 32 => -Strong,
        // Desktop, low profile desktop, mini tower, tower, all in one, mini PC, stick PC.
        3 or 4 or 6 or 7 or 13 or 35 or 36 => -Weak,
        // Main server chassis, rack mount, multi-system, blade, blade enclosure.
        17 or 23 or 25 or 28 or 29 => Strong,
        _ => 0,
    };

    private static string? ReadDmi(string name) => ReadText(Path.Combine("/sys/class/dmi/id", name));

    private static string? ReadText(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : null;

    // A device tree property holds NUL-separated strings.
    private static string[] ReadStrings(string path) =>
        File.Exists(path) ? File.ReadAllText(path).Split('\0').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray() : [];
}
