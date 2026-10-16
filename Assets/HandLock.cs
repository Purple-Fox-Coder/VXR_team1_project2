using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class HandLock : MonoBehaviour
{
    [Header("Hand Positions")]
    public Transform left_hand;
    public Transform right_hand;

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

    private const float ground_stick_speed = 2F; // keeps player on ground
    private const float epsilon = 0.001F;

    private CharacterController controller;

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

    public void BeginLeftGrip()
    {
        if (this.left_hand == null)
        {
            Debug.LogError("My left hand is gone! :(");
            return;
        }

        this.left_grip_point = this.left_hand.position;
        this.is_left_gripping = true;
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

        this.right_grip_point = this.right_hand.position;
        this.is_right_gripping = true;
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
}
