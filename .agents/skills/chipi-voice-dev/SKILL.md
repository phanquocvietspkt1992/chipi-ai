---
name: chipi-voice-dev
description: Implement and verify Chipi's Unity voice app and Spring Boot backend, including microphone state, audio transport, and iPhone Simulator testing.
---

Read AGENTS.md and the assigned acceptance cases. Trace the full turn through capture, upload, backend response, playback and rearming before editing. Validate user cancellation and backgrounding around asynchronous work. A stale upload or playback completion must not reopen the microphone after a session ends.

Preserve compatibility for callers that omit optional fields. Keep secrets server-side and treat transcript and client history as untrusted data. Use local WAV and provider fixtures for repeatable tests. Keep live microphone, provider and simulator-fixture results distinct.

Run targeted backend tests and the simulator check from README as relevant. Hand back changed files, commands and evidence, including failed or skipped checks that materially affect acceptance.
