using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Avalonia.Telemetry;

// Which AI coding agent, if any, ran the build. Agents mark the commands they
// run with environment variables, which MSBuild hands to every build. Only the
// agent's name from the list below is sent: never a variable's value.
//
// The rules are Vercel's detect-agent (github.com/vercel/detect-agent,
// agents.json version 1, as of 2026-10-29), in its order: the first match
// wins. Two are left out because they also match a person: Cursor sets
// CURSOR_TRACE_ID in every terminal, and Kiro's TERM_PROGRAM=kiro is told
// apart from a person only by a missing terminal, which a build task cannot
// see: MSBuild may run it in a worker process that has none.
//
// Six changes come from the unmerged detect-agent pull request
// feat/extend-agent-detection-coverage (2026-10-17), each checked against the
// agent's own code: Crush, Hermes and Grok Bot, GROK_AGENT=1 for Grok,
// GOOSE_TERMINAL for Goose, and Junie no longer matching JUNIE_DATA.
internal static class AiAgentHelper
{
    private static readonly (AiAgent Agent, Func<BuildEnvironment, bool> Matches)[] Rules =
    {
        // Crush sets CRUSH=1 on every command it runs (internal/shell/shell.go).
        (AiAgent.Crush, e => e.IsSet("CRUSH")),
        (AiAgent.Hermes, e => e.IsSet("HERMES_SESSION_ID") || e.IsSet("HERMES_AGENT")),
        // Before Cursor: Grok Bot's local executor also sets CURSOR_AGENT=1, and
        // its cloud shells set it with the two SAND_ flags.
        (AiAgent.GrokBot, e => e.HasValue("SAND_AGENT", "1", StringComparison.Ordinal)
            || (e.IsSet("CURSOR_AGENT") && e.HasValue("SAND_MANIFEST_V2", "1", StringComparison.Ordinal) && e.HasValue("SAND_SUPERVISOR_ENABLED", "1", StringComparison.Ordinal))),
        (AiAgent.Cursor, e => e.IsSet("CURSOR_AGENT") || e.HasValue("CURSOR_EXTENSION_HOST_ROLE", "agent-exec", StringComparison.Ordinal)),
        (AiAgent.Kimi, e => e.IsSet("KIMI_PLUGIN_ROOT")),
        // Before Claude Code: Grok runs Claude Code plugins and may set their
        // markers. GROK_AGENT=1 marks its terminal; other values choose a model.
        (AiAgent.Grok, e => e.HasValue("GROK_AGENT", "1", StringComparison.Ordinal) || e.IsSet("GROK_PLUGIN_ROOT") || e.IsSet("GROK_PLUGIN_DATA")),
        (AiAgent.GeminiCli, e => e.IsSet("GEMINI_CLI")),
        (AiAgent.Cline, e => e.IsSet("CLINE_ACTIVE")),
        (AiAgent.Codex, e => e.IsSet("CODEX_SANDBOX") || e.IsSet("CODEX_CI") || e.IsSet("CODEX_THREAD_ID") || e.IsSet("CODEX_SANDBOX_NETWORK_DISABLED")),
        (AiAgent.Antigravity, e => e.IsSet("ANTIGRAVITY_AGENT") || e.IsSet("ANTIGRAVITY_CLI_ALIAS")),
        (AiAgent.Augment, e => e.IsSet("AUGMENT_AGENT")),
        (AiAgent.OpenCode, e => e.IsSet("OPENCODE_CLIENT") || e.IsSet("OPENCODE")),
        // Goose sets GOOSE_TERMINAL=1 on the commands it runs.
        (AiAgent.Goose, e => e.IsSet("GOOSE_PROVIDER") || e.IsSet("GOOSE_TERMINAL")),
        // Junie's launcher exports its own path here. JUNIE_DATA is not used: it
        // is a data folder that can be set outside Junie.
        (AiAgent.Junie, e => e.IsSet("JUNIE_SHIM_PATH")),
        (AiAgent.Pi, e => PiAgentPath!.IsMatch(e.Get("PATH") ?? string.Empty)),
        (AiAgent.ClaudeCowork, e => e.IsSet("CLAUDE_CODE_IS_COWORK") && (e.IsSet("CLAUDECODE") || e.IsSet("CLAUDE_CODE"))),
        (AiAgent.ClaudeCode, e => e.IsSet("CLAUDECODE") || e.IsSet("CLAUDE_CODE")),
        (AiAgent.Replit, e => e.IsSet("REPL_ID")),
        (AiAgent.GitHubCopilot, e => e.IsSet("COPILOT_MODEL") || e.IsSet("COPILOT_ALLOW_ALL") || e.IsSet("COPILOT_GITHUB_TOKEN")),
        (AiAgent.OpenClaw, e => e.IsSet("OPENCLAW_SHELL")),
        (AiAgent.Devin, _ => File.Exists("/opt/.devin")),
    };

    // detect-agent's agent names, plus the names agents are seen using:
    // Claude Code sets AI_AGENT=claude-code_<version>_agent.
    private static readonly Dictionary<string, AiAgent> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["crush"] = AiAgent.Crush,
        ["hermes-agent"] = AiAgent.Hermes,
        ["grok-bot"] = AiAgent.GrokBot,
        ["cursor"] = AiAgent.Cursor,
        ["cursor-cli"] = AiAgent.Cursor,
        ["kimi"] = AiAgent.Kimi,
        ["grok"] = AiAgent.Grok,
        ["gemini_cli"] = AiAgent.GeminiCli,
        ["cline"] = AiAgent.Cline,
        ["codex_cli"] = AiAgent.Codex,
        ["antigravity"] = AiAgent.Antigravity,
        ["augment-cli"] = AiAgent.Augment,
        ["open_code"] = AiAgent.OpenCode,
        ["goose"] = AiAgent.Goose,
        ["junie"] = AiAgent.Junie,
        ["pi"] = AiAgent.Pi,
        ["cowork"] = AiAgent.ClaudeCowork,
        ["claude_code"] = AiAgent.ClaudeCode,
        ["claude-code"] = AiAgent.ClaudeCode,
        ["replit"] = AiAgent.Replit,
        ["github-copilot"] = AiAgent.GitHubCopilot,
        ["github-copilot-cli"] = AiAgent.GitHubCopilot,
        ["kiro"] = AiAgent.Kiro,
        ["openclaw"] = AiAgent.OpenClaw,
        ["devin"] = AiAgent.Devin,
    };

    // A name, then optionally a version: "devin@1" as detect-agent recommends,
    // or "claude-code_2-1-309_agent" as Claude Code sends it.
    private static readonly Regex NameAndVersion = new(@"^(?<name>.+?)(?:@.*|_\d.*)?$");

    private static readonly Regex PiAgentPath = new(@"\.pi[\\/]agent");

    public static AiAgent Detect(BuildEnvironment environment)
    {
        // The variable detect-agent promotes for any agent to name itself, so
        // it beats every other marker.
        if (environment.Get("AI_AGENT") is { } declared && declared.Trim().Length > 0)
        {
            var name = NameAndVersion.Match(declared.Trim()).Groups["name"].Value;
            return Names.TryGetValue(name, out var known) ? known : AiAgent.Other;
        }

        return Rules.FirstOrDefault(r => r.Matches(environment)).Agent;
    }
}
