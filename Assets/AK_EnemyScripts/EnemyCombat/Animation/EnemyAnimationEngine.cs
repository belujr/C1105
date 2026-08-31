using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CombatSystem.Animation
{
    [RequireComponent(typeof(Animator))]
    public class EnemyAnimationEngine : MonoBehaviour
    {
        private PlayableGraph playableGraph;
        private AnimationMixerPlayable mixerPlayable;
        private Animator animatorComponent;

        // Active playables used for blending/crossfading between two clips
        private AnimationClipPlayable currentPlayable;
        private AnimationClipPlayable nextPlayable;

        private bool isCrossfading = false;
        private float crossfadeTimer = 0f;
        private float crossfadeDuration = 0.1f;

        private void Awake()
        {
            animatorComponent = GetComponent<Animator>();
            
            // Ensure the animator doesn't require a legacy AnimatorController asset
            animatorComponent.runtimeAnimatorController = null;

            InitializePlayableGraph();
        }

        private void InitializePlayableGraph()
        {
            // 1. Create the Playable Graph
            playableGraph = PlayableGraph.Create($"PlayableGraph_{gameObject.name}");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            // 2. Create a mixer with 2 input ports (Slot 0: Current, Slot 1: Incoming)
            mixerPlayable = AnimationMixerPlayable.Create(playableGraph, 2);

            // 3. Create the animation output tied to our Animator component
            var playableOutput = AnimationPlayableOutput.Create(playableGraph, "AnimationOutput", animatorComponent);
            
            // 4. Connect our mixer to the output using SetSourcePlayable
            playableOutput.SetSourcePlayable(mixerPlayable);

            // 5. Start the graph
            playableGraph.Play();
        }

        /// <summary>
        /// Plays an animation clip with smooth crossfading driven entirely by C#.
        /// </summary>
        public void PlayAnimation(AnimationClip clip, float transitionDuration, float speed = 1.0f)
        {
            if (clip == null || !playableGraph.IsValid()) return;

            // Create a new playable instance for the requested clip
            AnimationClipPlayable newPlayable = AnimationClipPlayable.Create(playableGraph, clip);
            newPlayable.SetSpeed(speed);
            newPlayable.SetApplyFootIK(false);

            // If no animation is currently playing, instantly assign to Slot 0
            if (!currentPlayable.IsValid())
            {
                currentPlayable = newPlayable;
                playableGraph.Connect(currentPlayable, 0, mixerPlayable, 0);
                mixerPlayable.SetInputWeight(0, 1f);
                mixerPlayable.SetInputWeight(1, 0f);
                currentPlayable.Play();
                return;
            }

            // If we are already crossfading, cleanup the stale 'next' playable immediately
            if (nextPlayable.IsValid())
            {
                playableGraph.Disconnect(mixerPlayable, 1);
                nextPlayable.Destroy();
            }

            // Setup the new incoming playable in Slot 1
            nextPlayable = newPlayable;
            playableGraph.Connect(nextPlayable, 0, mixerPlayable, 1);
            nextPlayable.Play();

            crossfadeDuration = Mathf.Max(0.001f, transitionDuration);
            crossfadeTimer = 0f;
            isCrossfading = true;
        }

        private void Update()
        {
            if (!isCrossfading) return;

            crossfadeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(crossfadeTimer / crossfadeDuration);

            // Blend weights: Slot 0 fades out, Slot 1 fades in
            mixerPlayable.SetInputWeight(0, Mathf.Lerp(1f, 0f, t));
            mixerPlayable.SetInputWeight(1, Mathf.Lerp(0f, 1f, t));

            // When crossfade finishes
            if (t >= 1f)
            {
                isCrossfading = false;

                // 1. Disconnect and destroy the old current playable in Slot 0
                if (currentPlayable.IsValid())
                {
                    playableGraph.Disconnect(mixerPlayable, 0);
                    currentPlayable.Destroy();
                }

                // 2. Disconnect nextPlayable from Slot 1 BEFORE moving it to Slot 0
                if (nextPlayable.IsValid())
                {
                    playableGraph.Disconnect(mixerPlayable, 1);
                }

                // 3. Promote nextPlayable to be our new currentPlayable
                currentPlayable = nextPlayable;
                nextPlayable = default;

                // 4. Reset mixer weights (Slot 0 is 100%, Slot 1 is 0%)
                mixerPlayable.SetInputWeight(0, 1f);
                mixerPlayable.SetInputWeight(1, 0f);

                // 5. Connect the new current playable into Slot 0
                if (currentPlayable.IsValid())
                {
                    playableGraph.Connect(currentPlayable, 0, mixerPlayable, 0);
                }
            }
        }

        private void OnDestroy()
        {
            // Crucial: Playable graphs allocate native C++ memory and must be explicitly destroyed
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }
        }
    }
}