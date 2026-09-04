# Contributing

## House rules

- English only in code, comments and editor facing strings.
- Comments explain why, not what. If deleting a comment loses nothing, delete it.
- No `async void`. Use `UniTask` or `UniTaskVoid`.
- No `UnityEngine.Input`. Ask `StoryInput`.
- Every static field resets in `StoryKitLifecycle`. Domain reload is often
  disabled, and a stale static means the first play works and the second does
  not.
- Never pair anything by list index against a separately ordered list. Give the
  thing an id and let it own its own target.
- Anything the player can see or touch gets walked as a player before review.
