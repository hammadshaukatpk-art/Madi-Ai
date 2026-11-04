# Madi AI voice/chat regression checklist

This branch fixes the routing of app-name mentions in questions; it does **not** redesign the dashboard, replace the local language model, or claim full JARVIS autonomy.

## Desktop checks (Windows 10/11)

1. Unlock Madi AI; confirm the screenshot-based WPF layout is unchanged.
2. Type `What is MetaTrader 5?` — Madi should answer conversationally, not launch MT5.
3. Type `Can you explain YouTube monetization?` — Madi should answer conversationally, not open YouTube.
4. Type `Open YouTube` — Madi should open YouTube, with the existing Chrome profile selection.
5. Type `Open MetaTrader 5` — Madi should attempt to launch MT5.
6. Type `Volume up` — the volume changes and Madi audibly confirms it.
7. Type `Hello` — Madi audibly responds.
8. Ask two general questions in succession — the selected local model remains cached; the AI chat stays responsive while requests are in flight.
9. With Ollama stopped, ask a general question — an explicit connection error is shown, not a silent failure.
10. Test Voice Control > microphone input with English, Urdu, and Hindi. Check your installed Windows TTS voices; unavailable Urdu/Hindi voices still need proper fallback handling.
11. Use the existing voice smoke test to check Whisper initialization, and manually test a real microphone; synthetic tests alone do not validate the user's microphone, noise level or latency.

## Still outside this fix

- A voice-first streaming dialogue pipeline with barge-in and natural turn-taking.
- Tool planning and reliable autonomous multi-step desktop actions.
- A bundled model that provides ChatGPT-level intelligence offline.
- A pixel-perfect recreation of the screenshot.
- Verified end-to-end latency on the user's hardware.

These require additional integration testing; do not mark them complete based on installer success.
