# Product Mission (Lite)

Questward is a self-hosted todo app that helps people who struggle to start small chores
actually finish them, by paying out experience points scaled to how hard each task was
and levelling up a character as the list gets cleared.

Questward serves self-hosters who want their task list on their own hardware rather than
in someone else's SaaS. Unlike hosted gamified trackers, it runs as one container and
keeps every task, XP total and badge in a Postgres volume you control, and its progression
system is built to resist inflation: level is derived from the XP total rather than
stored, XP is snapshotted at the moment of completion, and completion is idempotent, so
the score can only be moved by doing the work.
