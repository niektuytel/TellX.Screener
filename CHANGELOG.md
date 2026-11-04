# Changelog

## 1.3.2

- Add GitHub Actions builds with direct ZIP artifacts, download summaries and version-tag releases.
- Publication cleanup: remove duplicated validation and centralize worker schedule evaluation.
- Add clean source packaging, SHA-256 checksums, repository ignore rules and repeatable verification.
- Keep manual Play after hours, automatic scheduling, activity filtering and webhook contracts unchanged.

## 1.3.1

- Play outside recording hours starts immediately and continues until Pause or app exit.
- Keep the session override when editing settings; restart uses the saved daily schedule again.

## 1.3.0

- Default recording window: 06:00 to 18:00, local Windows time.
- Skip capture without new mouse or keyboard activity.
- Handle request cancellation at the end time and fresh context after the overnight pause.

## 1.2.0

- Accept validated partial settings updates in webhook responses.
- Preserve the locally configured webhook destination and authentication.
