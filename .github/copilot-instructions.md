# Copilot Instructions — Avalonia.BuildServices

This repository implements **build-time telemetry collection and
transmission** for Avalonia. It runs inside MSBuild on developer and CI
machines, gathers anonymised metadata about the build, and sends it to
the Avalonia backend. It does **not** store data, expose HTTP APIs,
manage user accounts, or host user-facing services. Ignore generalised
GDPR guidance that applies to those concerns.

AvaloniaUI OÜ is an **Estonian controller**. Our lead supervisory
authority is the **Estonian Data Protection Inspectorate (Andmekaitse
Inspektsioon, AKI)**. Where generalised GDPR commentary takes a German
position (which is typically stricter than the EDPB consensus), we
follow the Estonian and EDPB reading instead. In practice that means we
take EDPB Opinion 28/2024 on anonymisation at face value: properly
hashed, high-entropy, non-correlatable telemetry is treated as anonymous
and outside the scope of the GDPR, as stated in our Privacy Policy.

When reviewing pull requests, apply the rules below in addition to the
usual code review concerns. Flag any deviation clearly.

---

# Telemetry Rules for Avalonia.BuildServices

Grounded in GDPR Articles 5, 25, 32, the Estonian Personal Data
Protection Act, EDPB guidelines (in particular Opinion 28/2024 on
anonymisation), and AKI practice. Also grounded in two internal
documents that take precedence for day-to-day decisions:

- The **Avalonia UI Privacy Policy** (v1.1, effective 5 February 2026).
  Source of truth for what we may collect, why, and on what legal basis.
- **Schedule 8: Avalonia Community Licence** (v1.0, effective 26 March
  2026). Source of truth for the Community tier's mandatory telemetry
  and compliance monitoring model.

> **Golden Rule:** Collect less. Hash high-entropy inputs. Transmit
> securely. Match what we collect to what the Privacy Policy and
> Schedule 8 actually say.

## 0. Legal basis and tier model

Telemetry in this codebase serves **two distinct business purposes**,
and every collection code path must be clearly attributable to one of
them.

**(a) Product improvement and framework development.** Applies to all
tiers. Anonymous, aggregated build-time metadata used to understand
framework usage and prioritise engineering work. Legal basis: this data
is anonymised to a standard we consider sufficient to fall outside the
scope of personal data regulation, as stated in Section 3 of the Privacy
Policy. Where residual re-identification risk exists, the fallback legal
basis is **legitimate interests** (GDPR Art. 6(1)(f)) in improving and
developing the framework.

**(b) Licence compliance monitoring for the Community tier.** Applies
only to builds using the free Community licence. Mandatory under
Schedule 8, Sections 6.1 and 6.2. Used to detect organisational use of
what is a strictly individual, non-commercial licence. Legal basis:
**performance of a contract** (GDPR Art. 6(1)(b)) because it is an
express condition of Schedule 8, supported by **legitimate interests**
(GDPR Art. 6(1)(f)) in enforcing our licence terms and protecting our
commercial model. This is equivalent to the JetBrains free-tier model
and is a defensible position under Estonian and EDPB practice.

**MUST**

- Every new telemetry field maps to purpose (a), purpose (b), or both,
  and the PR description states which.
- Fields used only for purpose (b) are gated so they are only collected
  when the build is using a Community-tier licence. Paid-tier builds do
  not collect compliance-only fields.
- If a field is added for purpose (b), the PR author confirms it is
  covered by the Privacy Policy's "Securing the Services" section and
  Schedule 8, Section 6. If it is not, update the Privacy Policy and
  Schedule 8 in the same PR.

**MUST NOT**

- Collect compliance-monitoring data from paid-tier users without an
  explicit, documented reason and a Privacy Policy update.
- Conflate the two purposes in a single undocumented field.

## 1. Data minimisation

**MUST**

- Every field in the telemetry payload has a documented reason for
  collection tied to purpose (a) or (b) above.
- Remove fields that are no longer used.
- Prefer coarse values (OS major version, architecture) over precise
  ones (full OS build, CPU serial) unless the coarse value is useless
  for the stated purpose.

**MUST NOT**

- Collect source code, project contents, file contents, or directory
  structures. Schedule 8, Section 6.4 is explicit on this.
- Collect email addresses, usernames, account identifiers, or any
  direct identifier in cleartext.
- Collect environment variables, command-line arguments, or secrets.
- Add a new telemetry field "just in case it's useful later."

## 2. Hashing and pseudonymisation

**MUST**

- Hash values that could be identifying (project name, machine name,
  user-controlled strings) with SHA-256 before transmission.
- Use a stable, anonymous GUID for the machine identifier. Derive it in
  a way that is stable across builds on the same machine but not
  derivable from hardware serials, MAC addresses, or hostnames in
  cleartext.
- Design the payload so that **field correlation cannot re-identify an
  individual**. A hashed machine name plus a hashed project name plus a
  precise timestamp is not anonymous. Either coarsen the timestamp,
  drop one of the hashes, or accept that the payload is pseudonymous
  rather than anonymous and treat it as personal data.

**MUST NOT**

- Use a weak hash (MD5, SHA-1).
- Use unsalted hashes of low-entropy inputs (e.g. email addresses,
  where a rainbow table will crack the hash trivially). Where an input
  is low-entropy, either do not collect it, or salt with a non-public,
  rotating salt.
- Claim a payload is "anonymous" without having done the correlation
  analysis.

## 3. Transparency and document alignment

The Privacy Policy and Schedule 8 are the legally binding documents.
The README is an engineering convenience. Engineering changes must not
drift ahead of either legal document.

**MUST**

- If a PR adds, removes, or changes a telemetry field, update the
  README's "What is collected?" and "What is not collected?" sections
  in the same PR.
- If the change materially expands what we collect or why, also flag
  in the PR description that the **Privacy Policy** and, where
  relevant, **Schedule 8** need to be reviewed. Legal alignment is a
  blocking review item, not a follow-up.
- Keep the paid-tier opt-out mechanism (`AVALONIA_TELEMETRY_OPTOUT=1`)
  documented and working.

**MUST NOT**

- Add a data point that is not disclosed in the README.
- Change collection behaviour in a way that makes the Privacy Policy
  or Schedule 8 inaccurate.

## 4. Opt-out, consent, and the Community tier

Avalonia has a tiered model. Telemetry behaviour differs by tier, and
the code must respect that.

**Paid tiers (Plus, Pro, Enterprise, XPF).** Opt-out via
`AVALONIA_TELEMETRY_OPTOUT=1`. Legal basis is legitimate interests in
product improvement, with opt-out as the balancing mechanism required
by the EDPB's guidance on Art. 6(1)(f).

**Community tier.** Telemetry is **mandatory** and is an express
condition of the licence under Schedule 8, Sections 6.1 and 6.2. There
is no opt-out. Legal basis is performance of contract (the customer has
agreed to Schedule 8 to receive the free licence) supported by
legitimate interests.

**MUST**

- For paid tiers, respect `AVALONIA_TELEMETRY_OPTOUT=1` at the
  earliest possible point, before any collection, hashing, or network
  activity.
- For paid tiers, fail closed on errors in the opt-out check: if in
  doubt, do not send.
- For the Community tier, make the mandatory-telemetry nature obvious
  in onboarding, README, and any account-level messaging. "Obvious" is
  a Schedule 8 condition: customers must know what they agreed to.
- Tier detection must itself be robust. A bug that misclassifies a
  paid user as Community and ignores their opt-out is a serious
  incident.

**MUST NOT**

- Require a paid-tier user to opt out more than once per machine or CI
  environment.
- Re-enable telemetry after a paid-tier opt-out without explicit user
  action.
- Silently collect compliance-monitoring data from paid-tier users.
- Add a "test mode" for the Community tier that bypasses the mandatory
  telemetry in production builds.

## 5. Transmission

**MUST**

- Use HTTPS (TLS 1.2+; prefer 1.3) for all telemetry transmission.
- Validate the server certificate. Never disable certificate checks,
  even for debugging. Any such code must be guarded behind a
  compile-time flag that is never shipped.
- Default the telemetry endpoint to an EEA region. Non-EEA transmission
  is permitted where an Article 46 safeguard (typically SCCs) is in
  place, consistent with Section 7 of our Privacy Policy. Any change to
  endpoint region requires a Privacy Policy and RoPA review in the
  same PR.

**MUST NOT**

- Send telemetry over plain HTTP.
- Add fallback transports (e.g. DNS exfiltration, third-party analytics
  SDKs) that bypass the documented endpoint.
- Send telemetry to a new endpoint or sub-processor without updating
  the Privacy Policy, the Sub-Processors list, and the RoPA.

## 6. Logging (MSBuild task, collector, inspector)

**MUST**

- Use `BuildEngine?.LogMessageEvent` for build task output. Never
  `Console.WriteLine` from the MSBuild task.
- Keep log output terse and free of PII: no raw machine names, no raw
  project paths, no user names.
- Log *events* ("telemetry sent", "opt-out detected", "tier: community,
  mandatory telemetry active"), not payload contents.

**MUST NOT**

- Log the full telemetry payload at any level that ships by default.
- Log licence keys, hardware IDs, or any value before it is hashed.
- Log environment variables or command lines.

## 7. Failure behaviour

**MUST**

- Swallow network and serialisation errors silently at build time.
  Telemetry failure must never break a user's build, including a
  Community-tier user's build, even though their telemetry is
  mandatory. Enforcement of Schedule 8 happens server-side and via
  compliance review, not by blocking the build.
- Time out network calls aggressively (seconds, not minutes) so a slow
  endpoint cannot stall builds.

**MUST NOT**

- Retry indefinitely.
- Write failure dumps to disk containing payload data.
- Surface telemetry errors to the developer.

## 8. Compliance monitoring specifics (Community tier)

Schedule 8, Section 6.3 commits us to automated detection with **human
review before any enforcement action**. The code in this repository
must support that commitment.

**MUST**

- Keep compliance-signal fields (those indicative of organisational
  use, e.g. CI environment detection, corporate network heuristics)
  clearly identified in code and documentation.
- Ensure the server-side pipeline that acts on these signals has a
  documented human-review step before any account is flagged,
  suspended, or invoiced. This is a Schedule 8 Section 6.3 commitment;
  automated enforcement without human review is a contract breach.
- Ensure the seven-day notice period for termination under Schedule 8,
  Section 8.2 is reflected in any tooling that suspends Community
  accounts.

**MUST NOT**

- Build client-side logic that acts on suspected non-compliance (e.g.
  disabling features, nagging users). Enforcement is a commercial
  process, not a runtime behaviour.
- Collect compliance signals from paid-tier users.

## 9. Secrets and endpoints

**MUST**

- Treat any API key, signing key, or endpoint credential as a secret,
  loaded from build-time configuration, never hard-coded in a public
  source file.
- Add new secret file patterns to `.gitignore`: `.env`, `.env.*`,
  `*.pem`, `*.key`, `*.pfx`, `*.p12`, `secrets/`.

**MUST NOT**

- Commit API keys, tokens, or production endpoint credentials.
- Include secrets in log output, even at trace level.

## 10. Testing

**MUST**

- Use synthetic values (fake project names, fake GUIDs,
  `@example.com` emails) in tests and sample payloads.
- Point test builds at a non-production endpoint or disable
  transmission entirely.
- Test the tier-detection path explicitly. A paid user being treated
  as Community, or vice versa, is a category of bug we will not
  tolerate.

**MUST NOT**

- Commit real collected telemetry payloads as test fixtures.
- Run the real collector against production from CI.

## 11. PR Review Checklist

### Payload (`TelemetryPayload.cs`, `TelemetryWriter.cs`)

- [ ] Every new field maps to purpose (a) product improvement, (b)
  Community-tier compliance, or both, and the PR description says so.
- [ ] Any new field is documented in `README.md` under "What is
  collected?" and, where legally relevant, flagged for Privacy Policy
  or Schedule 8 review.
- [ ] Any new identifying-ish field is hashed (SHA-256) before
  transmission, with attention to correlation risk across fields.
- [ ] No source code, file paths, env vars, or command-line args leak
  into the payload.

### Tier handling

- [ ] Tier detection is robust and tested.
- [ ] Compliance-only fields are gated to Community tier.
- [ ] Paid-tier `AVALONIA_TELEMETRY_OPTOUT=1` is honoured before any
  collection or hashing, and no code path bypasses it.
- [ ] Community-tier mandatory telemetry is not bypassable in shipping
  builds.

### Transmission

- [ ] HTTPS only; certificate validation intact.
- [ ] Endpoint is EEA by default, or non-EEA with SCCs referenced and
  Privacy Policy alignment confirmed.
- [ ] Failures are silent and time-bounded; a user's build cannot be
  broken by telemetry.

### Logging

- [ ] Uses `BuildEngine?.LogMessageEvent`, not `Console.WriteLine`,
  from the MSBuild task.
- [ ] No raw machine or project names, licence keys, or payload
  contents in logs.

### Compliance monitoring

- [ ] Any change to compliance-signal collection has a corresponding
  note on the server-side human-review step.
- [ ] No client-side enforcement logic (feature disabling, nagging,
  build breaking) has been introduced.

### Secrets

- [ ] No API keys, tokens, or credentials committed.
- [ ] No new secret-shaped files outside `.gitignore`.

---

> **Golden Rule:** Collect less. Hash high-entropy inputs. Transmit
> securely. Match what we collect to what the Privacy Policy and
> Schedule 8 actually say.