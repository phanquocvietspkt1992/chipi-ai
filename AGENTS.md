# Chipi

Chipi is a Unity 6000.6.7f1 English-practice app with a Spring Boot voice backend. Validate the iPhone Simulator first. The approved character reference is `Unity/Assets/Resources/ChipiConcept.png`.

## Development workflow

For work requested through the Chipi workflow, delegate requirements to `chipi_po`, implementation to `chipi_dev`, and independent verification to `chipi_qc`. QC plans checks while Dev implements, then verifies the delivered build. See [workflow and handoffs](docs/agent-workflow.md). Treat the agents as one development team. PO owns the acceptance matrix, Dev owns the build and fixes, and QC independently signs off the build. The coordinating agent assigns one owner per file and shared device, resolves blockers and reports evidence once. For a full QA request, inventory all user-visible functions before testing and mark every case PASS, FAIL or BLOCKED with evidence. Deploy to the simulator only unless the user explicitly requests a physical-device deployment for that change.

Use the user's direction and existing behavior to resolve routine choices. Prefer direct feedback and concrete tradeoffs. Ask for input only when a material product decision, permission or external dependency is blocking progress.

Keep provider credentials on the backend. Use demo audio and local provider fixtures for routine verification. Describe what ran on the simulator and what requires a live microphone or provider separately. See [README](README.md) for build and launch commands.

For voice playback or transport changes, Dev runs `node scripts/check_simulator_voice.js` on the updated simulator build and QC independently reruns it before sign-off. Treat failures as defects and unavailable checks as BLOCKED; fixture results do not prove physical-device or live-provider behavior.

Keep response-time measurements and performance diagnostics in developer logs and reports only. Never surface them in the Chipi app UI.

For visible interface changes, PO, Dev, and QC apply the shared [mobile UX/UI skill](.agents/skills/chipi-mobile-ux/SKILL.md) when writing acceptance criteria, implementing, and reviewing the simulator build.
