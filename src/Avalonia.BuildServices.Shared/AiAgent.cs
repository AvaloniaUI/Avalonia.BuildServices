namespace Avalonia.Telemetry;

// The AI coding agent that ran the build; see AiAgentHelper. Other is an agent
// that named itself in AI_AGENT with a name not on the list.
public enum AiAgent
{
    None,
    Other,
    ClaudeCode,
    ClaudeCowork,
    Codex,
    Cursor,
    GeminiCli,
    GitHubCopilot,
    Junie,
    OpenCode,
    Antigravity,
    Augment,
    Cline,
    Devin,
    Goose,
    Grok,
    Kimi,
    Kiro,
    OpenClaw,
    Pi,
    Replit,
    Crush,
    Hermes,
    GrokBot,
}