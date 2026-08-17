# Spec Summary (Lite)

Add Auth0-backed user accounts so several people can share one Questward instance, each
with their own tasks, XP, character and badges. The API validates JWT bearer tokens
against the Auth0 issuer and resolves the `sub` claim to a local user record, while
tasks, characters and achievement unlocks all gain a `UserId` and every query is scoped
to the caller. Sign-up is open and sign-in requires outbound internet, both deliberate
and both documented.
