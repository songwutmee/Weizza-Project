// Sample StoryScript file. Save = auto-compiles to a FlowGraphAsset.
// Drag the imported asset into a FlowRunner (or a SubgraphNode) and play.

=== intro ===
[baker happy]: Good morning! You must be the new courier.   #INTRO_001
[baker]: I have a <wave>very special</wave> delivery today.   #INTRO_002
[baker]: Interested?   #INTRO_003

* Sure, I'll take it!   #INTRO_C1
    [player]: Sure, I'll take it!   #INTRO_004
    << var.set met_baker 1 >>
    << quest.start bakery_delivery >>
    -> accepted
* Maybe later...   #INTRO_C2 {baker_mood >= 0}
    [baker]: Oh... alright then.   #INTRO_005
    << var.add baker_mood -1 >>
    -> END

=== accepted ===
[baker]: <shake>Wonderful!</shake> Here's the package.   #ACCEPTED_001
<< give package_sweetbread 1 >>
<< event Quest.Accepted bakery_delivery >>
[baker]: Deliver it before it gets cold!   #ACCEPTED_002
<< save >>
-> END
