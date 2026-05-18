#!/usr/bin/env python3
"""
Daily tide report emailer for 136th Street, Ocean City, MD.
Uses NOAA Tides and Currents API (station 8570283 — Ocean City Inlet).
"""

import os
import sys
import smtplib
import json
import urllib.request
import urllib.parse
from datetime import date, datetime
from email.mime.multipart import MIMEMultipart
from email.mime.text import MIMEText

import anthropic

NOAA_STATION = "8570283"  # Ocean City (Inlet), MD
SEASON_START = (5, 17)    # May 17
SEASON_END   = (10, 15)   # October 15


def in_season(today: date) -> bool:
    start = date(today.year, *SEASON_START)
    end   = date(today.year, *SEASON_END)
    return start <= today <= end


def fetch_tides(today: date) -> list[dict]:
    date_str = today.strftime("%Y%m%d")
    params = urllib.parse.urlencode({
        "begin_date": date_str,
        "end_date":   date_str,
        "station":    NOAA_STATION,
        "product":    "predictions",
        "datum":      "MLLW",
        "time_zone":  "lst_ldt",
        "interval":   "hilo",
        "units":      "english",
        "application":"tide_email",
        "format":     "json",
    })
    url = f"https://api.tidesandcurrents.noaa.gov/api/prod/datagetter?{params}"
    with urllib.request.urlopen(url, timeout=15) as resp:
        data = json.loads(resp.read().decode())
    if "error" in data:
        raise RuntimeError(f"NOAA API error: {data['error']['message']}")
    return data["predictions"]


def generate_narrative(today: date, tides: list[dict]) -> str:
    tide_lines = []
    for entry in tides:
        t = datetime.strptime(entry["t"], "%Y-%m-%d %H:%M")
        label = "High" if entry["type"] == "H" else "Low"
        height = float(entry["v"])
        tide_lines.append(f"  {label} tide at {t.strftime('%-I:%M %p')}: {height:+.2f} ft")

    message = (
        f"Today is {today.strftime('%A, %B %-d, %Y')}. "
        f"Here are today's tide predictions:\n" + "\n".join(tide_lines)
    )

    client = anthropic.Anthropic()
    session = client.beta.sessions.create(
        agent=os.environ["TIDE_AGENT_ID"],
        environment_id=os.environ["TIDE_ENV_ID"],
    )

    parts: list[str] = []
    with client.beta.sessions.events.stream(session_id=session.id) as stream:
        client.beta.sessions.events.send(
            session_id=session.id,
            events=[{
                "type": "user.message",
                "content": [{"type": "text", "text": message}],
            }],
        )
        for event in stream:
            if event.type == "agent.message":
                for block in event.content:
                    if block.type == "text":
                        parts.append(block.text)
            elif event.type in ("session.status_terminated", "session.status_idle"):
                break

    return "".join(parts).strip()


def tide_row(entry: dict) -> str:
    t = datetime.strptime(entry["t"], "%Y-%m-%d %H:%M")
    label = "High Tide" if entry["type"] == "H" else "Low Tide "
    height = float(entry["v"])
    time_12 = t.strftime("%-I:%M %p")
    bar_len = max(0, int((height + 1) * 6))  # simple ASCII bar
    bar = "█" * bar_len
    return (
        f"  {label}  |  {time_12:>8}  |  {height:+.2f} ft  |  {bar}"
    )


def build_html(today: date, tides: list[dict], narrative: str) -> str:
    date_label = today.strftime("%A, %B %-d, %Y")
    rows = ""
    for entry in tides:
        t = datetime.strptime(entry["t"], "%Y-%m-%d %H:%M")
        label = "High Tide" if entry["type"] == "H" else "Low Tide"
        height = float(entry["v"])
        time_12 = t.strftime("%-I:%M %p")
        color = "#1a6b3c" if entry["type"] == "H" else "#2c5f8a"
        rows += f"""
        <tr>
          <td style="padding:10px 16px;font-weight:bold;color:{color};">{label}</td>
          <td style="padding:10px 16px;">{time_12}</td>
          <td style="padding:10px 16px;text-align:right;">{height:+.2f} ft</td>
        </tr>"""

    return f"""<!DOCTYPE html>
<html>
<head><meta charset="utf-8"></head>
<body style="font-family:Georgia,serif;background:#f0f6ff;margin:0;padding:24px;">
  <div style="max-width:520px;margin:0 auto;background:#fff;border-radius:10px;
              box-shadow:0 2px 8px rgba(0,0,0,.12);overflow:hidden;">

    <div style="background:#0a3d6b;color:#fff;padding:24px 28px;">
      <div style="font-size:22px;font-weight:bold;">🌊 Daily Tide Report</div>
      <div style="font-size:14px;margin-top:4px;opacity:.85;">
        136th Street — Ocean City, MD
      </div>
      <div style="font-size:13px;margin-top:2px;opacity:.70;">{date_label}</div>
    </div>

    <div style="padding:20px 28px 8px;font-size:15px;line-height:1.65;color:#333;">
      {narrative}
    </div>

    <div style="padding:0 28px 20px;">
      <table style="width:100%;border-collapse:collapse;">
        <thead>
          <tr style="border-bottom:2px solid #d0dde8;">
            <th style="padding:8px 16px;text-align:left;color:#555;font-size:13px;">Tide</th>
            <th style="padding:8px 16px;text-align:left;color:#555;font-size:13px;">Time (ET)</th>
            <th style="padding:8px 16px;text-align:right;color:#555;font-size:13px;">Height</th>
          </tr>
        </thead>
        <tbody style="font-size:15px;">{rows}
        </tbody>
      </table>
    </div>

    <div style="padding:12px 28px 20px;font-size:12px;color:#888;">
      Predictions from NOAA Tides &amp; Currents, Station 8570283 (Ocean City Inlet, MD).
      Heights above Mean Lower Low Water (MLLW). Times in Eastern Time.
    </div>
  </div>
</body>
</html>"""


def build_text(today: date, tides: list[dict], narrative: str) -> str:
    date_label = today.strftime("%A, %B %-d, %Y")
    lines = [
        "Daily Tide Report — 136th Street, Ocean City, MD",
        date_label,
        "",
        narrative,
        "",
        f"  {'Tide':<11}  {'Time (ET)':<10}  Height",
        "  " + "-" * 40,
    ]
    for entry in tides:
        lines.append(tide_row(entry))
    lines += [
        "",
        "Source: NOAA Tides & Currents, Station 8570283 (Ocean City Inlet, MD)",
        "Heights above MLLW. Times in Eastern Time.",
    ]
    return "\n".join(lines)


def parse_recipients(raw: str) -> list[str]:
    import re
    return [addr.strip() for addr in re.split(r"[,;]", raw) if addr.strip()]


def send_email(subject: str, html: str, text: str,
               sender: str, password: str, recipient: str) -> None:
    recipients = parse_recipients(recipient)
    msg = MIMEMultipart("alternative")
    msg["Subject"] = subject
    msg["From"]    = sender
    msg["To"]      = sender
    msg.attach(MIMEText(text, "plain"))
    msg.attach(MIMEText(html, "html"))

    with smtplib.SMTP("smtp.gmail.com", 587) as server:
        server.ehlo()
        server.starttls()
        server.login(sender, password)
        server.sendmail(sender, recipients, msg.as_string())


def main() -> None:
    today = date.today()

    if not in_season(today):
        print(f"Outside season ({today}). No email sent.")
        sys.exit(0)

    gmail_address  = os.environ["GMAIL_ADDRESS"]
    gmail_password = os.environ["GMAIL_APP_PASSWORD"]
    recipient      = os.environ["RECIPIENT_EMAIL"]

    print(f"Fetching NOAA tides for {today} …")
    tides = fetch_tides(today)

    print("Generating tide narrative via Claude …")
    narrative = generate_narrative(today, tides)

    subject = f"Tide Report — OC 136th St — {today.strftime('%a %b %-d')}"
    html    = build_html(today, tides, narrative)
    text    = build_text(today, tides, narrative)

    print(f"Sending email to {recipient} …")
    send_email(subject, html, text, gmail_address, gmail_password, recipient)
    print("Done.")


if __name__ == "__main__":
    main()
