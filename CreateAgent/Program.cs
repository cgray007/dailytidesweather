// One-time setup — creates the OC Tide Narrator Managed Agent and its cloud
// environment. Run once with ANTHROPIC_API_KEY set, then add the two printed
// IDs as GitHub Secrets: TIDE_AGENT_ID and TIDE_ENV_ID.

using Anthropic;
using Anthropic.Models.Beta.Agents;
using Anthropic.Models.Beta.Environments;
using DailyTidesWeather.Shared;

// On local runs, persist any set secrets and hydrate any missing ones
// from the per-user store; no-op in GitHub Actions.
LocalSecrets.Sync();

var client = new AnthropicClient(); // reads ANTHROPIC_API_KEY from env

Console.WriteLine("Creating cloud environment …");
var env = await client.Beta.Environments.Create(new EnvironmentCreateParams
{
    Name = "tide-narrator-env",
    Config = new BetaCloudConfigParams
    {
        Networking = new BetaUnrestrictedNetwork(),
    },
});
Console.WriteLine($"  environment id : {env.ID}");

Console.WriteLine("Creating OC Tide Narrator agent …");
var agent = await client.Beta.Agents.Create(new AgentCreateParams
{
    Name = "OC Tide Narrator",
    Model = BetaManagedAgentsModel.ClaudeOpus4_7,
    System =
        "You are a friendly local beach guide at 136th Street in Ocean City, MD. "
        + "Given NOAA tide predictions for the day, write a 2-3 sentence morning "
        + "briefing that helps the reader plan their beach day. Cover the best windows "
        + "for swimming (near high tide), low-tide opportunities for shelling or sandbar "
        + "exploration, and any noteworthy patterns. Be warm, practical, and concise. "
        + "Do not open with a greeting or 'Good morning'. "
        + "Respond with only the briefing text — no extra commentary.",
});
Console.WriteLine($"  agent id       : {agent.ID}");
Console.WriteLine($"  agent version  : {agent.Version}");
Console.WriteLine();
Console.WriteLine("Next steps:");
Console.WriteLine("  1. Go to github.com/cgray007/claudecode → Settings → Secrets and variables → Actions");
Console.WriteLine($"  2. Add secret  TIDE_ENV_ID   = {env.ID}");
Console.WriteLine($"  3. Add secret  TIDE_AGENT_ID = {agent.ID}");
