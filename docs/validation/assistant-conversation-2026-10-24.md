# Conversational assistant update

## Behavior and contract

The academic assistant endpoint now calls the configured remote model even when
retrieval returns no documents. It supports greetings, general explanations,
writing and programming questions. The prompt includes the server's current date,
weekday and time in Asia/Ho_Chi_Minh (UTC+07:00).

Existing authorization, active-account checks, project access checks, rate limits
and transactional query history/audit remain in place. No mutation tools are
provided to the model. Each request is still an independent question, without
conversation history or live web/weather access.

Response semantics (no schema/migration change):

| answerType | generationStatus | Meaning |
| --- | --- | --- |
| GENERAL | COMPLETED | General model knowledge or server date/time; no institutional citations |
| GROUNDED | COMPLETED | Answer supported by selected authorized sources |
| INSUFFICIENT_EVIDENCE | INSUFFICIENT_EVIDENCE | Missing evidence for institutional/project claims |
| FALLBACK | FALLBACK | Retrieved excerpts, or an unavailable message if no excerpts exist |

Model output must be valid bounded JSON. Citation IDs must exist in retrieved
evidence; GENERAL and INSUFFICIENT_EVIDENCE cannot carry citations. Only selected
citations are displayed, while the full retrieved evidence remains in the saved
run snapshot. General answers are labelled as reference information on the FE.

The system prompt requires official evidence for school-specific rules, deadlines,
scores and private project facts. This semantic classification is model-driven;
authorization and the absence of mutation tools are enforced by backend code.
No guarantee of perfect model classification or factual accuracy is implied.

## Verification

- Release compilation with warnings as errors; 13 academic SQL integration tests
  passed on an owned isolated database, including authorization, history, citation
  validation, general questions without documents and provider failure.
- 3 provider unit tests passed.
- FE typecheck/build and all 21 AI component/API tests passed. Lint completed with
  existing repository warnings; the build retains bundle-size warnings.
- Real FE login and model requests against staging passed on desktop and mobile:
  - Run 12: current date, GENERAL, correct Saturday 2026-10-24.
  - Run 13: REST API explanation, GENERAL.
  - Run 14: current weather, GENERAL, explicitly states no live weather access.
  - Run 16: DEMO rubric/scheme explanation, GROUNDED, one supporting citation.
  - Run 17: exact official school deadline, INSUFFICIENT_EVIDENCE.
  - Run 18: request to change grade, refused without claiming a mutation.
- DB readback confirms the six recorded statuses. Browser evidence is in
  `outputs/assistant-conversation-20261024/` in the parent workspace.

## Deployment and rollback

Staging image: `aipms-api:assistant-conversation-20261024` (healthy).
Previous image retained: `aipms-api:066f588-academic`.
Only the API container was recreated; no SQL migration or data reset was needed.
The local FE at localhost:5175 loads the updated labels from the task branch.
Rollback by selecting the previous image in the private compose environment and
recreating only the API with --no-build --no-deps. Preserve all query history.

## Signed-in profile follow-up

The model now receives `currentUser.displayName` from the active database user,
so it can answer questions about the signed-in user's name. No email, password,
token or other user's profile is added. The display name is explicitly treated
as data rather than instructions. This does not add conversation memory.

All 14 academic integration tests passed, including exact profile-field isolation.
Staging image `aipms-api:assistant-profile-20261024` is healthy; previous
`assistant-conversation-20261024` remains available for rollback. A real browser
login and "Ten toi la gi?" question returned HTTP 200, GENERAL, zero citations,
run 25; the answer matches the logged-in test account's display name.
