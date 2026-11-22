# Chipi voice QC reference

Pick rows that cover the requested acceptance, the behavior changed, and relevant regressions. Keep hardware, provider and playback results separate.

| Check | Test input and environment | Pass evidence |
|---|---|---|
| Backend contract | Maven tests, provider simulation and WAV fixture | Valid input, context order and bounds, invalid input, compatible response |
| App integration | iPhone Simulator with WAV fixture, specify demo or live server | Request sent, response received, nonempty playable audio, playback completed, expected next state |
| Physical iPhone microphone | Microphone allowed, built-in route, known spoken phrase | Frames arrive with a measurable signal, speech is detected and endpointing sends the turn after the speaker stops |
| Physical iPhone live conversation | Same physical device, real provider, two intelligible test phrases | Recognition matches the spoken meaning, response uses the prior turn when relevant, playback completes, mic rearms, ending the session clears context |
| Intermediate English coaching | Live or on-device model. Say “Yesterday I go to the gym,” “My shift ran late, so I’m tired,” and “Why is it ‘went’?” as directed test turns. Include a deliberately unclear recording. | Models “went” without waiting to be asked, responds naturally to correct speech, explains the rule when asked, and asks for a repeat if the input is unclear. Verify that the spoken correction is easy to follow and the conversation continues. Demo playback cannot pass this check. |
| Failures and lifecycle | Change only what is applicable: permission denied, silence, mid-sentence pause, 429/503, cancellation, backgrounding | Clear status, no silent upload, no late callback reopening the microphone after ending, and recovery appropriate to the error |

For microphone defects, trace permission → hardware route → native frame count and peak → Unity frame count and peak → voice detection → HTTP status. A route and advancing frame count with native peak zero fail the capture case even if the simulator passes. Apple Voice Memos is a comparison when a person is available. Keep audio samples, transcripts, tokens and credentials out of shared reports unless specifically approved. Use designated sample phrases and ports, and keep cloud checks within the authorized tier.

Record target/build, backend mode, input, steps, expected result, result and smallest supporting log excerpt or test output. Mark unavailable required evidence BLOCKED. A paid provider test, a device install, and a mic test each have their own prerequisites.

## Sources

- [Apple: testing on Simulator and hardware devices](https://developer.apple.com/documentation/xcode/testing-in-simulator-versus-testing-on-hardware-devices)
- [Apple AVAudioEngine input node](https://developer.apple.com/documentation/avfaudio/avaudioengine/inputnode)
- [Unity Test Framework: Edit Mode versus Play Mode](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/edit-mode-vs-play-mode-tests.html)
- [Scrum Guide: Definition of Done](https://scrumguides.org/scrum-guide.html#definition-of-done)
