using System.Numerics;

namespace NinjaSlayer.Code.Combat;

internal sealed class FreeControlMotor
{
    internal const float RunSpeed = 420f;
    internal const float Gravity = 1800f;
    internal const float JumpSpeed = 850f;
    internal const float DashSpeed = 1200f;
    internal const float DashSeconds = .18f;
    internal const float CollisionDamageThreshold = 2500f;
    internal const float MaximumDamageSpeed = 10000f;
    internal const int MaximumCollisionDamage = 10;
    internal Vector2 Velocity;
    internal bool HasAirJump { get; private set; } = true;
    internal bool HasAirDash { get; private set; } = true;
    internal bool Dashing => _dashRemaining > 0f;
    private float _coyote, _buffer, _dashRemaining, _dashCooldown, _wallLock;
    private float _dashDirection;
    private bool _wasGrounded;

    internal void Reset(Vector2 velocity = default)
    {
        Velocity = velocity;
        _coyote = _buffer = _dashRemaining = _dashCooldown = _wallLock = 0f;
        _wasGrounded = false;
        HasAirJump = HasAirDash = true;
    }

    internal void Step(float dt, float axis, bool jumpPressed, bool jumpHeld, bool dashPressed,
        bool fastFall, bool grounded, float wallNormal, float facing)
    {
        _buffer = jumpPressed ? .1f : Math.Max(0f, _buffer - dt);
        _coyote = grounded ? .08f : Math.Max(0f, _coyote - dt);
        _dashCooldown = Math.Max(0f, _dashCooldown - dt);
        _wallLock = Math.Max(0f, _wallLock - dt);
        if (grounded && !_wasGrounded) HasAirJump = HasAirDash = true;
        _wasGrounded = grounded;
        if (dashPressed && _dashCooldown <= 0f && (grounded || HasAirDash))
        {
            _dashRemaining = DashSeconds;
            _dashCooldown = .45f;
            _dashDirection = axis == 0f ? facing : Math.Sign(axis);
            HasAirDash = false;
        }
        if (_dashRemaining > 0f)
        {
            _dashRemaining = Math.Max(0f, _dashRemaining - dt);
            Velocity = new(_dashDirection * DashSpeed, 0f);
            return;
        }
        bool wallSlide = wallNormal != 0f && axis * wallNormal < 0f && Velocity.Y > 0f;
        if (_wallLock <= 0f)
        {
            float target = fastFall && grounded ? 0f : axis * RunSpeed;
            float acceleration = grounded ? 7000f : 3500f;
            Velocity.X = MoveTowards(Velocity.X, target, acceleration * dt);
        }
        if (grounded && Velocity.Y > 0f) Velocity.Y = 0f;
        else Velocity.Y = Math.Min(2400f, Velocity.Y + Gravity * (fastFall ? 2.5f : 1f) * dt);
        if (wallSlide) Velocity.Y = Math.Min(Velocity.Y, 160f);
        if (_buffer > 0f && (_coyote > 0f || wallNormal != 0f || HasAirJump))
        {
            if (wallNormal != 0f && !grounded)
            {
                Velocity.X = wallNormal * 520f;
                _wallLock = .1f;
                HasAirJump = HasAirDash = true;
            }
            else if (_coyote <= 0f) HasAirJump = false;
            Velocity.Y = -JumpSpeed;
            _buffer = _coyote = 0f;
        }
        if (!jumpHeld && Velocity.Y < -JumpSpeed * .4f) Velocity.Y = -JumpSpeed * .4f;
    }

    private static float MoveTowards(float from, float to, float step) =>
        from + Math.Clamp(to - from, -step, step);

    internal static int CollisionDamage(float relativeSpeed)
    {
        if (relativeSpeed < CollisionDamageThreshold) return 0;
        float progress = Math.Clamp((relativeSpeed - CollisionDamageThreshold)
            / (MaximumDamageSpeed - CollisionDamageThreshold), 0f, 1f);
        return 1 + (int)MathF.Floor((MaximumCollisionDamage - 1) * progress * progress);
    }

    internal static int AttackTier(float heldSeconds) => heldSeconds < .15f ? 0 : heldSeconds < .5f ? 1 : 2;
}
