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

    [Header("Collision")]
    public Transform head;
    [Min(0.01F)]
    public float head_radius = 0.12F;
    public float hand_radius = 0.05F;
    public LayerMask collision_mask = ~0;
    public LayerMask collision_exclude_mask = 0;

    [Header("Interactables")]
    [Tooltip("Layers whose non-kinematic rigidbodies may be moved by grip")]
    public LayerMask moveable_mask = 0;
    [Range(0F, 1F)]
    public float object_force_scale = 1F;

    private class Grip
    {
        public bool active;
        public Vector3 point;
        public Rigidbody body;
        public Vector3 local_point;
        public bool on_body;

        public Vector3 Anchor => this.on_body && this.body != null
                            ? this.body.transform.TransformPoint(this.local_point)
                            : this.point;
    }

    private readonly Grip left_grip = new Grip();
    private readonly Grip right_grip = new Grip();

    private const float ground_stick_speed = 2F; // keeps player on ground
    private const float epsilon = 0.001F;

    private CharacterController controller;

    private readonly Collider[] grip_hits = new Collider[8];
    private readonly Collider[] collision_hits = new Collider[8];
    private readonly Dictionary<int, LayerGripSound> sound_lookup = new Dictionary<int, LayerGripSound>();
    private SphereCollider probe;

    private Vector3 velocity;

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

    private Vector3 DepenetrateSphere(Vector3 center, float radius)
    {
        int count = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            this.collision_hits,
            this.collision_mask & ~this.collision_exclude_mask,
            QueryTriggerInteraction.Ignore
        );

        this.probe.radius = radius / Mathf.Max(epsilon, this.probe.transform.lossyScale.x);

        Vector3 position = center;

        for (int i = 0; i < count; ++i)
        {
            Collider hit = this.collision_hits[i];
            if (hit.transform.IsChildOf(this.transform))
                continue;

            Rigidbody held = hit.attachedRigidbody;
            if (held != null && (held == this.left_grip.body || held == this.right_grip.body))
                continue;

            if (Physics.ComputePenetration(
                this.probe, position, Quaternion.identity,
                hit, hit.transform.position, hit.transform.rotation,
                out Vector3 direction, out float distance
            ))
            {
                position += direction * distance;
            }
        }

        return position;
    }

    private void PushOut(Transform tracked, float radius)
    {
        if (tracked == null)
            return;

        Vector3 push = this.DepenetrateSphere(tracked.position, radius) - tracked.position;

        if (push.sqrMagnitude < epsilon * epsilon)
            return;

        this.controller.Move(Vector3.ClampMagnitude(push, this.max_correction_per_frame));

        Vector3 normal = push.normalized;
        float   into   = Vector3.Dot(this.velocity, normal);

        if (into < epsilon)
            this.velocity -= normal * into;
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

        visual.position = gripping ? grip_point : this.DepenetrateSphere(tracked.position, this.hand_radius);
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

    private void BeginGrip(Grip grip, Transform hand)
    {
        if (!this.CanGrip(hand, out Collider surface))
            return;

        Rigidbody rb = surface.attachedRigidbody;

        bool moveable = rb != null
                       && !rb.isKinematic
                       && ((1 << surface.gameObject.layer) & this.moveable_mask.value) != 0;
        grip.active = true;
        grip.point = hand.position;
        grip.body = moveable ? rb : null;
        grip.on_body = moveable;
        if (moveable)
        {
            grip.local_point = rb.transform.InverseTransformPoint(grip.point);
            this.SetIgnoreHeld(rb, true);
        }

        this.PlayGripSound(surface, grip.point);
    }

    public void BeginLeftGrip()
    {
        if (this.left_hand != null)
            this.BeginGrip(this.left_grip, this.left_hand);
    }

    public void EndLeftGrip()
    {
        this.ReleaseGrip(this.left_grip, this.right_grip);
    }

    public void BeginRightGrip()
    {
        if (this.right_hand != null)
            this.BeginGrip(this.right_grip, this.right_hand);
    }

    public void EndRightGrip()
    {
        this.ReleaseGrip(this.right_grip, this.left_grip);
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
    private Vector3 ArmForce(Transform hand, Grip grip, float damping, float dt)
    {
        if (grip.on_body && grip.body == null)
        {
            grip.active = false;
            grip.on_body = false;
            return Vector3.zero;
        }

        Vector3 anchor = grip.Anchor;
        Rigidbody rb = grip.body;
        Vector3 anchor_vel = rb != null ? rb.GetPointVelocity(anchor) : Vector3.zero;
    
        float k = this.grip_stiffness;
        float c = damping;

        if (rb != null)
        {
            Vector3 it = rb.inertiaTensor;
            float i_min = Mathf.Max(epsilon, Mathf.Min(it.x, Mathf.Min(it.y, it.z)));
            Vector3 r = anchor - rb.worldCenterOfMass;
            float inv_obj = 1F / rb.mass + r.sqrMagnitude / i_min;

            float reduced = 1F / (1F / this.mass + inv_obj);

            float step = Mathf.Max(dt, Time.fixedDeltaTime);
            k = Mathf.Min(k, 0.25F * reduced / (step * step));
            c = Mathf.Min(c, 0.5F * reduced / step);
        }


        Vector3 spring = Vector3.ClampMagnitude(
            (anchor - hand.position) * k,
            this.max_arm_force);
        Vector3 damp = -c * (this.velocity - anchor_vel);
        Vector3 total = spring + damp;

        if (grip.body != null)
            grip.body.AddForceAtPosition(-total * this.object_force_scale * dt, anchor, ForceMode.Impulse);

        return total;
    }

    private void SetIgnoreHeld(Rigidbody rb, bool ignore)
    {
        if (rb == null)
            return;

        foreach (Collider col in rb.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(this.controller, col, ignore);
    }

    private void ReleaseGrip(Grip grip, Grip other)
    {
        Rigidbody rb = grip.body;
        grip.active = false;
        grip.body = null;
        grip.on_body = false;

        bool other_holds_it = other.active && other.body == rb;
        if (rb != null && !other_holds_it)
            this.SetIgnoreHeld(rb, false);
    }

    private Vector3 ForceGrip(float dt)
    {
        bool l = this.left_grip.active  && this.left_hand  != null;
        bool r = this.right_grip.active && this.right_hand != null;
        if (!l && !r)
            return Vector3.zero;

        float damping = this.grip_damping / ((l ? 1 : 0) + (r ? 1 : 0));

        Vector3 force = Vector3.zero;
        if (l)
            force += this.ArmForce(this.left_hand, this.left_grip, damping, dt);
        if (r)
            force += this.ArmForce(this.right_hand, this.right_grip, damping, dt);
        
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
        force += this.ForceGrip(dt);
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

        this.PushOut(this.head, this.head_radius);

        if (!this.left_grip.active)
            this.PushOut(this.left_hand, this.hand_radius);
        if (!this.right_grip.active)
            this.PushOut(this.right_hand, this.hand_radius);
    }

    void Awake()
    {
        GameObject probe_object = new GameObject("CollisionProbe");
        probe_object.transform.SetParent(this.transform, false);
        this.probe = probe_object.AddComponent<SphereCollider>();
        this.probe.isTrigger = true;
        
        this.BuildSoundLookup();
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
                            this.left_grip.active, this.left_grip.Anchor);
        this.PlaceVisualHand(this.right_hand_visual, this.right_hand,
                            this.right_grip.active, this.right_grip.Anchor);
    }
}
