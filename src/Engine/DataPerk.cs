using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // One instance per perks-sheet row. The game stores and calls these like its own Techniques
    // (for whichever team owns them); every reaction is run by the Engine from the row's data.
    public class DataPerk : Technique
    {
        public PerkDef Def { get; private set; }

        public void Init(PerkDef def)
        {
            Def = def;
            name = def.Title;
            hideFlags = HideFlags.DontUnloadUnusedAsset;
        }

        public bool Listens(string trigger) => System.Array.IndexOf(Def.Triggers, trigger) >= 0;

        public override void OnStart(GameObject thisObject) { }
        public override void OnBump(BallMovement ball, Transform position) { }
        public override void OnTip(BallMovement ball) { }

        // Spike power was decided in the DoSpike prefix (conditions, banks, streaks).
        public override float OnSpike(BallMovement ball, Rigidbody2D rb, Transform position) =>
            Engine.SpikePowerShare(this, position != null ? position.GetComponent<PlayerController>() : null);

        public override void OnServe(BallMovement ball, Transform position)
        {
            var pc = position != null ? position.GetComponent<PlayerController>() : null;
            if (pc != null && Listens("serve")) Engine.FirePerk(this, "serve", pc, ball);
        }

        public override float OnJump(Transform position)
        {
            var pc = position != null ? position.GetComponent<PlayerController>() : null;
            return pc != null && Listens("jump") ? Engine.FirePerk(this, "jump", pc, null) : 0f;
        }

        public override float OnBlockJump()
        {
            var pc = Engine.CurrentInput;
            return pc != null && Listens("block_jump") ? Engine.FirePerk(this, "block_jump", pc, null) : 0f;
        }
    }
}
