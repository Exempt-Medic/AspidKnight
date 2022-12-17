using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Modding;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using SFCore.Utils;
using System;
using System.Reflection;

namespace AspidKnight
{
    public class AspidKnightMod : Mod
    {
        private static AspidKnightMod? _instance;

        internal static AspidKnightMod Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException($"An instance of {nameof(AspidKnightMod)} was never constructed");
                }
                return _instance;
            }
        }

        public override string GetVersion() => GetType().Assembly.GetName().Version.ToString();

        public AspidKnightMod() : base("AspidKnight")
        {
            _instance = this;
        }

        private static readonly MethodInfo HCMove = typeof(HeroController).GetMethod("Move", BindingFlags.NonPublic | BindingFlags.Instance);
        private ILHook? ilHCMove;

        public override void Initialize()
        {
            Log("Initializing");

            On.PlayMakerFSM.OnEnable += AspidFriendly;
            On.HeroController.Awake += GameStartChanges;
            On.GetNailDamage.OnEnter += ReduceNailDamage;
            On.HutongGames.PlayMaker.Actions.IntCompare.OnEnter += ChangeHatchlingCost;
            On.HutongGames.PlayMaker.Actions.SetScale.OnEnter += SpellSizes;

            ilHCMove = new ILHook(HCMove, AirSpeeds);

            Log("Initialized");
        }

        private void AirSpeeds(ILContext il)
        {
            ILCursor cursor = new ILCursor(il).Goto(0);

            cursor.TryGotoNext(i => i.MatchLdfld<HeroController>("RUN_SPEED_CH_COMBO"));
            cursor.GotoNext();
            cursor.EmitDelegate<Func<float, float>>(speed => speed * (HeroController.instance.cState.onGround ? 1.0f : 1.5f));

            cursor.TryGotoNext(i => i.MatchLdfld<HeroController>("RUN_SPEED_CH"));
            cursor.GotoNext();
            cursor.EmitDelegate<Func<float, float>>(speed => speed * (HeroController.instance.cState.onGround ? 1.0f : 1.5f));

            cursor.TryGotoNext(i => i.MatchLdfld<HeroController>("RUN_SPEED"));
            cursor.GotoNext();
            cursor.EmitDelegate<Func<float, float>>(speed => speed * (HeroController.instance.cState.onGround ? 1.0f : 1.5f));
        }

        private void SpellSizes(On.HutongGames.PlayMaker.Actions.SetScale.orig_OnEnter orig, HutongGames.PlayMaker.Actions.SetScale self)
        {
            // Vengeful Spirit
            if (self.Fsm.GameObject.name == "Fireball(Clone)" && self.Fsm.Name == "Fireball Control" && self.State.Name == "Set Damage")
            {
                if (self.State.ActiveActionIndex == 0)
                {
                    self.x.Value = 0.65f;
                    self.y.Value = 0.65f;
                }

                else if (self.State.ActiveActionIndex == 6)
                {
                    self.x.Value = 0.845f;
                    self.y.Value = 1.04f;
                }
            }
            
            // Shade Soul
            else if (self.Fsm.GameObject.name == "Fireball2 Spiral(Clone)" && self.Fsm.Name == "Fireball Control" && self.State.Name == "Set Damage" && self.State.ActiveActionIndex == 0)
            {
                self.x.Value = 1.17f;
                self.y.Value = 1.17f;
            }

            orig(self);
        }

        private void ChangeHatchlingCost(On.HutongGames.PlayMaker.Actions.IntCompare.orig_OnEnter orig, HutongGames.PlayMaker.Actions.IntCompare self)
        {
            if (self.Fsm.GameObject.name == "Charm Effects" && self.Fsm.Name == "Hatchling Spawn" && self.State.Name == "Can Hatch?")
            {
                self.integer2.Value = 4;
            }

            orig(self);
        }

        private void ReduceNailDamage(On.GetNailDamage.orig_OnEnter orig, GetNailDamage self)
        {
            orig(self);

            if (self.Fsm.GameObject.name == "Attacks" && self.Fsm.Name == "Set Slash Damage" && self.State.Name == "Get Damage")
            {
                self.storeValue.Value = (int)(self.storeValue.Value * 0.85f);
            }
        }

        private void GameStartChanges(On.HeroController.orig_Awake orig, HeroController self)
        {
            orig(self);

            // Give Monarch Wings
            PlayerData.instance.hasDoubleJump = true;

            // Set Attack Speeds
            HeroController.instance.ATTACK_COOLDOWN_TIME = 0.4715f;
            HeroController.instance.ATTACK_COOLDOWN_TIME_CH = 0.2875f;
        }

        private void AspidFriendly(On.PlayMakerFSM.orig_OnEnable orig, PlayMakerFSM self)
        {
            orig(self);

            #region Aspid Mothers
            if (self.Fsm.GameObject.name.StartsWith("Hatcher") && self.Fsm.Name == "Hatcher")
            {
                self.AddFsmState("Friendly");
                self.AddFsmState("Unfriendly");
                self.ChangeFsmTransition("Idle", "ALERT", "Friendly");
                self.AddFsmTransition("Friendly", "TOOK DAMAGE", "Unfriendly");
                self.AddFsmTransition("Unfriendly", "FINISHED", "Distance Fly");

                self.AddFsmAction("Friendly", new SetDamageHeroAmount()
                {
                    target = new FsmOwnerDefault()
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner
                    },
                    damageDealt = 0
                });

                self.AddFsmAction("Unfriendly", new SetDamageHeroAmount()
                {
                    target = new FsmOwnerDefault()
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner
                    },
                    damageDealt = 1
                });
            }
            #endregion
            #region Aspid Hunters
            else if (self.Fsm.GameObject.name.StartsWith("Spitter") && self.Fsm.Name == "spitter")
            {
                self.AddFsmState("Friendly");
                self.AddFsmState("Unfriendly");
                self.AddFsmBoolVariable("Friendly").Value = true;
                self.AddFsmTransition("Alert", "FRIENDLY", "Friendly");
                self.AddFsmTransition("Friendly", "TOOK DAMAGE", "Unfriendly");
                self.AddFsmTransition("Unfriendly", "FINISHED", "Alert");

                self.AddFsmAction("Alert", new BoolTest()
                {
                    boolVariable = self.GetFsmBoolVariable("Friendly"),
                    isTrue = FsmEvent.GetFsmEvent("FRIENDLY"),
                    isFalse = null,
                    everyFrame = false
                });

                self.AddFsmAction("Friendly", new SetDamageHeroAmount()
                {
                    target = new FsmOwnerDefault()
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner
                    },
                    damageDealt = 0
                });

                self.AddFsmAction("Unfriendly", new SetBoolValue()
                {
                    boolVariable = self.GetFsmBoolVariable("Friendly"),
                    boolValue = false,
                    everyFrame = false
                });

                self.AddFsmAction("Unfriendly", new SetDamageHeroAmount()
                {
                    target = new FsmOwnerDefault()
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner
                    },
                    damageDealt = 1
                });
            }
            #endregion
            #region Primal Aspids
            else if (self.Fsm.GameObject.name.StartsWith("Super Spitter") && self.Fsm.Name == "spitter")
            {
                self.AddFsmState("Friendly");
                self.AddFsmState("Unfriendly");
                self.AddFsmBoolVariable("Friendly").Value = true;
                self.AddFsmTransition("Alert", "FRIENDLY", "Friendly");
                self.AddFsmTransition("Friendly", "TOOK DAMAGE", "Unfriendly");
                self.AddFsmTransition("Unfriendly", "FINISHED", "Alert");

                self.InsertFsmAction("Alert", new BoolTest()
                {
                    boolVariable = self.GetFsmBoolVariable("Friendly"),
                    isTrue = FsmEvent.GetFsmEvent("FRIENDLY"),
                    isFalse = null,
                    everyFrame = false
                }, 4);

                self.AddFsmAction("Friendly", new SetDamageHeroAmount()
                {
                    target = new FsmOwnerDefault()
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner
                    },
                    damageDealt = 0
                });

                self.AddFsmAction("Unfriendly", new SetBoolValue()
                {
                    boolVariable = self.GetFsmBoolVariable("Friendly"),
                    boolValue = false,
                    everyFrame = false
                });

                self.AddFsmAction("Unfriendly", new SetDamageHeroAmount()
                {
                    target = new FsmOwnerDefault()
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner
                    },
                    damageDealt = 1
                });
            }
            #endregion
        }
    }
}
