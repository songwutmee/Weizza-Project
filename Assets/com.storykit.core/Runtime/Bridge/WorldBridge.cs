// The world bridge: how graphs learn about the physical game.
//
// StoryKit never reads physics directly. Instead, small bridge components sit
// on GameObjects, watch the world (triggers, collisions, interactions) and
// translate what happens into StoryEvents and blackboard values. Graphs listen
// to those. This one-way street means:
//   - your player controller, jump and physics code stays entirely yours
//   - graphs stay engine-agnostic and testable
//   - designers wire "player entered the cave" without code
//
// The components themselves are one per file, because Unity binds a .cs file to
// the single class whose name matches it and will not offer the others in the
// Add Component menu:
//
//   StoryTriggerZone.cs         raises an event when something enters a collider
//   StoryCollisionForwarder.cs  the same for collisions rather than triggers
//   StoryInteractable.cs        a thing the player can walk up to and use
//   BlackboardValuePusher.cs    mirrors a component value onto the blackboard
//
// This file is the overview those four share. It declares nothing itself.
