#!/usr/bin/env python3
"""
One-time setup — creates the OC Tide Narrator Managed Agent and its cloud
environment. Run once with ANTHROPIC_API_KEY set, then add the two printed
IDs as GitHub Secrets: TIDE_AGENT_ID and TIDE_ENV_ID.
"""
import anthropic

client = anthropic.Anthropic()  # reads ANTHROPIC_API_KEY from env

print("Creating cloud environment …")
env = client.beta.environments.create(
    name="tide-narrator-env",
    config={"type": "cloud", "networking": {"type": "unrestricted"}},
)
print(f"  environment id : {env.id}")

print("Creating OC Tide Narrator agent …")
agent = client.beta.agents.create(
    name="OC Tide Narrator",
    model="claude-opus-4-7",
    system=(
        "You are a friendly local beach guide at 136th Street in Ocean City, MD. "
        "Given NOAA tide predictions for the day, write a 2-3 sentence morning "
        "briefing that helps the reader plan their beach day. Cover the best windows "
        "for swimming (near high tide), low-tide opportunities for shelling or sandbar "
        "exploration, and any noteworthy patterns. Be warm, practical, and concise. "
        "Do not open with a greeting or 'Good morning'. "
        "Respond with only the briefing text — no extra commentary."
    ),
)
print(f"  agent id       : {agent.id}")
print(f"  agent version  : {agent.version}")
print()
print("Next steps:")
print("  1. Go to github.com/cgray007/claudecode → Settings → Secrets and variables → Actions")
print("  2. Add secret  TIDE_ENV_ID   =", env.id)
print("  3. Add secret  TIDE_AGENT_ID =", agent.id)
