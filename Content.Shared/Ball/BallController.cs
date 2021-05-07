using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Player;

namespace Content.Shared.Ball
{
    [UsedImplicitly]
    public class BallController : VirtualController
    {
        public override List<Type> UpdatesBefore { get; } = new()
        {
            typeof(ArenaController),
        };

        public override void UpdateAfterSolve(bool prediction, float frameTime)
        {
            base.UpdateAfterSolve(prediction, frameTime);

            foreach (var (_, transform, physics) in ComponentManager.EntityQuery<BallComponent, ITransformComponent, PhysicsComponent>())
            {
                var y = transform.WorldPosition.Y;

                // Reflect velocity on collision with arena.
                if (!(y > 0) || !(y < SharedPongSystem.ArenaBox.Height))
                {
                    physics.LinearVelocity *= new Vector2(1, -1);
                    SoundSystem.Play(Filter.Broadcast(), "/Audio/bloop.wav", AudioParams.Default);
                }

                var maxSpeed = EntitySystem.Get<BallSystem>().BallMaximumSpeed;

                // Ensure ball doesn't go above the maximum speed.
                if (physics.LinearVelocity.Length > maxSpeed)
                    physics.LinearVelocity = physics.LinearVelocity.Normalized * maxSpeed;
            }
        }
    }
}