using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class HandLock : MonoBehaviour
{
    [Serializable]
    public class LayerGripSound
    {
        [Tooltip("Layers that use this sound (can select multiple)")]
        public LayerMask layers;
        [Tooltip("One is picked at random each grab")]
        public AudioClip[] clips;
        [Range(0F, 1F)]
        public float volume = 1F;
    }

    [Header("Hand Positions")]
    public Transform left_hand;
    public Transform right_hand;
    public Transform left_hand_visual;
    public Transform right_hand_visual;

    [Header("Physics")]
    [Tooltip("Mass in Kg")]
    [Min(1F)]
    public float mass = 75F;
    [Tooltip("Gravity, not strictly up and down; m/s^2")]
    public Vector3 gravity = new Vector3(0F, -9.81F, 0F);
    [Tooltip("Friction coefficient against the ground, unitless")]
    [Min(0F)]
    public float ground_friction = 0.8F;

    [Header("Grip")]
    [Tooltip("in N/m")]
    [Min(0F)]
    public float grip_stiffness = 3000F;
    [Tooltip("in N*s/m, ~2*sqrt(k*m) is critically damped")]
    [Min(0F)]
    public float grip_damping = 600F;
    [Tooltip("In N, per arm")]
    [Min(0F)]
    public float max_arm_force = 1800F;

    [Header("Grip correction")]
    [Tooltip("Prevent extreme movement (m per frame)")]
    public float max_correction_per_frame = 0.5F;

    [Header("Air Drag")]
    [Tooltip("In kg/m^3")]
    [Min(0F)]
    public float air_density = 1.225F;
    [Min(0F)]
    public float drag_coefficient = 0.2F;

    [Header("Estimated body area as a plane (m^2)")]
    public float body_area = 0.5F;

    [Header("Grip Actions")]
    public InputActionReference left_grip_action;
    public InputActionReference right_grip_action;

    [Header("Grip Surface Settings")]
    [Tooltip("Layers that can be grabbed")]
    public LayerMask grip_mask = ~0;
    [Tooltip("Layers to exclude")]
    public LayerMask grip_exclude_mask = 0;
    [Tooltip("How far a hand can be from a surface but still register a grab")]
    [Min(0F)]
    public float grip_reach = 0.08F;

    [Header("Grip Sounds")]
    [Tooltip("Sound per layer.  First entry containing the layer wins.")]
    public List<LayerGripSound> layer_sounds = new List<LayerGripSound>();
    [Tooltip("Played when the grabbed layer has no sound defined above")]
    public AudioClip default_grip_sound;
    [Range(0F, 1F)]
    public float default_grip_volume = 1F;

    private const float ground_stick_speed = 2F; // keeps player on ground
    private const float epsilon = 0.001F;

    private CharacterController controller;
    private readonly Collider[] grip_hits = new Collider[8];
    private readonly Dictionary<int, LayerGripSound> sound_lookup = new Dictionary<int, LayerGripSound>();

    private Vector3 velocity;
    private Vector3 left_grip_point;
    private Vector3 right_grip_point;

    private bool is_left_gripping;
    private bool is_right_gripping;
    private bool was_grounded;

    void OnEnable()
    {
        this.left_grip_action?.action.Enable();
        this.right_grip_action?.action.Enable();
    }

    void OnDisable()
    {
        this.left_grip_action?.action.Disable();
        this.right_grip_action?.action.Disable();
    }

    private void BuildSoundLookup()
    {
        this.sound_lookup.Clear();

        foreach (LayerGripSound entry in this.layer_sounds)
        {
            if (entry == null)
                continue;

            for (int layer = 0; layer < 32; ++layer)
            {
                if ((entry.layers.value & (1 << layer)) == 0)
                    continue;

                if (!this.sound_lookup.ContainsKey(layer))
                    this.sound_lookup.Add(layer, entry);
            }
        }
    }

    private void PlayGripSound(Collider surface, Vector3 position)
    {
        AudioClip clip = this.default_grip_sound;
        float volume = this.default_grip_volume;

        // theoretically allow for variations
        if (this.sound_lookup.TryGetValue(surface.gameObject.layer, out LayerGripSound entry)
            && entry.clips != null && entry.clips.Length > 0)
        {
            AudioClip picked = entry.clips[UnityEngine.Random.Range(0, entry.clips.Length)];

            if (picked != null)
            {
                clip = picked;
                volume = entry.volume;
            }
        }

        if (clip == null)
            return;

        AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    private void PlaceVisualHand(Transform visual, Transform tracked,
                                bool gripping, Vector3 grip_point)
    {
        if (visual == null || tracked == null)
            return;

        visual.position = gripping ? grip_point : tracked.position;
        visual.rotation = tracked.rotation;
    }

    private bool CanGrip(Transform hand, out Collider surface)
    {
        surface = null;
        
        int count = Physics.OverlapSphereNonAlloc(
            hand.position,
            this.grip_reach,
            this.grip_hits,
            this.grip_mask & ~this.grip_exclude_mask,
            QueryTriggerInteraction.Ignore
        );

        float best = float.MaxValue;

        for (int i = 0; i < count; ++i)
        {
            Collider hit = this.grip_hits[i];
            
            if (hit.transform.IsChildOf(this.transform))
                continue;

            float dist = (hit.ClosestPoint(hand.position) - hand.position).sqrMagnitude;

            if (dist < best)
            {
                best = dist;
                surface = hit;
            }
        }

        return surface != null;
    }

    public void BeginLeftGrip()
    {
        if (this.left_hand == null)
        {
            Debug.LogError("My left hand is gone! :(");
            return;
        }

        if (!this.CanGrip(this.left_hand, out Collider surface))
            return;

        this.left_grip_point = this.left_hand.position;
        this.is_left_gripping = true;
        this.PlayGripSound(surface, this.left_grip_point);
    }

    public void EndLeftGrip()
    {
        this.is_left_gripping = false;
    }

    public void BeginRightGrip()
    {
        if (this.right_hand == null)
        {
            Debug.LogError("My right hand is gone! :(");
            return;
        }

        if (!this.CanGrip(this.right_hand, out Collider surface))
            return;

        this.right_grip_point = this.right_hand.position;
        this.is_right_gripping = true;
        this.PlayGripSound(surface, this.right_grip_point);
    }

    public void EndRightGrip()
    {
        this.is_right_gripping = false;
    }

    private void UpdateGripInput()
    {
        if (this.left_grip_action != null)
        {
            InputAction action = this.left_grip_action.action;

            if (action.WasPressedThisFrame())
                this.BeginLeftGrip();

            if (action.WasReleasedThisFrame())
                this.EndLeftGrip();
        }
        if (this.right_grip_action != null)
        {
            InputAction action = this.right_grip_action.action;

            if (action.WasPressedThisFrame())
                this.BeginRightGrip();

            if (action.WasReleasedThisFrame())
                this.EndRightGrip();
        }
    }

    // One arm as a spring: pulls the body so the hand returns to
    // where it grabbed. Capped by how hard an arm can really pull.
    private Vector3 ArmForce(Transform hand, Vector3 grip_point)
    {
        Vector3 stretch = grip_point - hand.position;

        return Vector3.ClampMagnitude(
            stretch * this.grip_stiffness,
            this.max_arm_force
        );
    }

    private Vector3 ForceGrip()
    {
        Vector3 force = Vector3.zero;

        if (!this.is_left_gripping && !this.is_right_gripping)
            return force;

        if (this.is_left_gripping && this.left_hand != null)
            force += this.ArmForce(this.left_hand, this.left_grip_point);

        if (this.is_right_gripping && this.right_hand != null)
            force += this.ArmForce(this.right_hand, this.right_grip_point);

        // Without this the spring just oscillates forever which is...
        // needless to say..... undesirable.  If a little funny
        force -= this.grip_damping * this.velocity;

        return force;
    }

    private Vector3 ForceDrag()
    {
        float speed = this.velocity.magnitude;

        // arbitrarily small epsilon, floating point error correction :)
        if (speed < epsilon)
            return Vector3.zero;

        return -0.5F
               * this.air_density
               * this.drag_coefficient
               * this.body_area
               * speed
               * this.velocity;
    }

    // Opposes sliding, never enough to reverse direction in one step
    private Vector3 ForceFriction(float dt)
    {
        Vector3 flat = new Vector3(this.velocity.x, 0F, this.velocity.z);
        float speed = flat.magnitude;

        if (!this.was_grounded || speed < epsilon)
            return Vector3.zero;

        float normal   = this.mass * this.gravity.magnitude;
        float max_stop = this.mass * speed / dt;
        float strength = Mathf.Min(this.ground_friction * normal, max_stop);

        return -strength * (flat / speed);
    }

    private void ResolveContact(CollisionFlags flags, Vector3 actual, float dt)
    {
        // Phasing is bad >:I  No ghost
        if ((flags & CollisionFlags.Above) != 0 && this.velocity.y > 0F)
            this.velocity.y = 0F;

        if ((flags & CollisionFlags.Below) != 0 && this.velocity.y < 0F)
            this.velocity.y = 0F;

        // Hit a wall: keep only the movement that really happened
        if ((flags & CollisionFlags.Sides) != 0)
        {
            this.velocity.x = actual.x / dt;
            this.velocity.z = actual.z / dt;
        }
    }

    private void Simulate(float dt)
    {
        // Forces accumulate as vectors since they are directional :)
        Vector3 force = this.mass * this.gravity;

        force += this.ForceDrag();
        force += this.ForceGrip();
        force += this.ForceFriction(dt);

        // Second law of motion; F = ma  (semi-implicit Euler)
        this.velocity += (force / this.mass) * dt;

        if (this.was_grounded && this.velocity.y < 0F)
            this.velocity.y = -ground_stick_speed;

    Vector3 step = Vector3.ClampMagnitude(
            this.velocity * dt,
            this.max_correction_per_frame
        );

        Vector3        before = this.transform.position;
        CollisionFlags flags  = this.controller.Move(step);
        Vector3        actual = this.transform.position - before;

        this.ResolveContact(flags, actual, dt);
        this.was_grounded = (flags & CollisionFlags.Below) != 0
                            || this.controller.isGrounded;
    }

    void Awake()
    {
        this.controller   = GetComponent<CharacterController>();
        this.was_grounded = this.controller.isGrounded;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (dt <= 0F)
            return;

        this.UpdateGripInput();
        this.Simulate(dt);
    }

    void LateUpdate()
    {
        this.PlaceVisualHand(this.left_hand_visual, this.left_hand,
                            this.is_left_gripping, this.left_grip_point);
        this.PlaceVisualHand(this.right_hand_visual, this.right_hand,
                            this.is_right_gripping, this.right_grip_point);
    }
}
