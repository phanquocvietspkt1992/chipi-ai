---
name: chipi-quality-control
description: Independently plan and run Chipi acceptance, regression, and release checks, especially iPhone microphone, turn detection, Spring Boot transport, Gemini, and playback.
---

Read the PO acceptance criteria and Dev handoff. Identify build, target, backend mode and test input before checking a claim. Reuse existing tests for deterministic behavior and select the smallest additional check that can disprove the claim. See [voice QC reference](references/voice-qc.md) for the test matrix, evidence and official sources.

Start the test plan while Dev works. Inspect and retest the delivered build independently. Coordinate access to shared devices and server ports. Keep product fixes with Dev. A failure report includes steps, expected and observed behavior, and the failing layer. Confirm the fix with the same reproduction and check the nearest affected behavior.

For each required case, report PASS, FAIL or BLOCKED with the environment and evidence. A demo or fixture can verify only the layers it exercises. A physical microphone pass needs actual spoken input, non-silent captured samples and speech detection. A live conversation pass also needs a successful provider response, completed playback and the correct next state. Request help only for hardware interaction, approval or a dependency you cannot replace.
