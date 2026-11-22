---
name: chipi-mobile-ux
description: Design, implement, or independently review Chipi's portrait iPhone UI, including visual hierarchy, dialogue readability, safe areas, and voice control states.
---

Use this when changing visible layout, navigation, typography, conversation copy, or controls in Chipi. Compare against the current simulator build and the user's latest approved design. The conversation and its controls are primary. Keep the smaller Chipi title and approved character recognizable. Keep diagnostics and response times in developer logs.

- **PO:** Define the content priority, what should be visible at default and compact sizes, and what happens with long or missing text. Specify the main action for Ready, Listening, Thinking and Speaking. Hand Dev and QC observable criteria.
- **Dev:** Use the iOS safe area. Give reply and transcript enough width and height to wrap, and provide access to overflow without shifting primary controls off screen. Aim for 44×44 pt hit areas. Avoid solving overflow by shrinking body text. Preserve the approved reference and current control behavior unless the task changes them.
- **QC:** Inspect the exact version on a portrait iPhone simulator. Compare the main screen against the prior build. Use a short greeting, a long reply and transcript, and listening or speaking states. Check clipping, overlap, spacing, safe areas, reachability and real scroll behavior where tooling supports it. Report edits verified by screenshots separately from interaction or accessibility checks that were not run.

Use Apple's [layout guidance](https://developer.apple.com/design/human-interface-guidelines/layout), [typography guidance](https://developer.apple.com/design/human-interface-guidelines/typography), and [touch target guidance](https://developer.apple.com/design/tips/) for platform conventions. Unity IMGUI uses explicit styles and layout, so verify actual behavior before claiming Dynamic Type or VoiceOver support.
