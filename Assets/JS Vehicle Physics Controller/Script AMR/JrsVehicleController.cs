// MIT License
//
// Copyright (c) 2023 Samborlang Pyrtuh
//
// Permission is hereby granted, free of charge, to any person obtaining a copy...
// (License text retained)

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using TMPro;

public class JrsVehicleController : MonoBehaviour
{
    [Header("Transmission Settings")]
    public bool isAutomatic = true;
    public float[] gearRatios;
    public float shiftThreshold = 5000f; // Max RPM (Upshift / Rev Limiter)
    public float downshiftThreshold = 2000f; // Min RPM to downshift
    private int currentGear = 1; // -1 = Reverse, 0 = Neutral, 1+ = Forward Gears

    [Header("UI Settings")]
    public TMP_Text speedText;
    public TMP_Text gearText;

    [Header("Vehicle Settings")]
    public float motorForce = 50f;
    public float maxSteerAngle = 30f;
    public WheelCollider frontLeftWheel, frontRightWheel, rearLeftWheel, rearRightWheel;
    public Transform frontLeftWheelTransform, frontRightWheelTransform, rearLeftWheelTransform, rearRightWheelTransform;

    public GameObject centerOfMassObject;
    private Rigidbody rb;
    public WheelCollider[] wheelCollidersBrake;
    public float brakeForce = 500f;
    public float engineBrakingForce = 300f; // Base force applied when downshifting aggressively

    public bool enable4x4 = false;

    public ParticleSystem frontLeftDustParticleSystem, frontRightDustParticleSystem, rearLeftDustParticleSystem, rearRightDustParticleSystem;
    private Quaternion prevRotation;

    private JrsInputController mobileInputController;

    [Header("Audio")]
    public AudioSource engineAudioSource;
    private AudioClip engineSound;
    private float targetPitch;
    public AudioSource engineStartAudioSource;

    [SerializeField] private bool hasStartedMoving = false;
    private LogitechControls controls;

    void Start()
    {
        controls = new LogitechControls();
        controls.CarInput.Enable();
        rb = GetComponent<Rigidbody>();
        prevRotation = frontLeftWheelTransform.rotation;

        mobileInputController = FindObjectOfType<JrsInputController>();

        engineSound = Resources.Load<AudioClip>("EngineSound");
        if (engineAudioSource != null)
        {
            targetPitch = engineAudioSource.pitch;
        }

        currentGear = isAutomatic ? 1 : 0;
        StartCoroutine(DelayedEngineSound());
    }

    IEnumerator DelayedEngineSound()
    {
        while (!hasStartedMoving)
        {
            yield return null;
        }
        yield return new WaitForSeconds(2f);

        if (engineAudioSource != null)
        {
            engineAudioSource.Play();
        }
    }

    private void OnEnable()
    {
        if (controls == null)
            controls = new LogitechControls();

        controls.CarInput.Enable();
    }

    private void OnDisable()
    {
        controls.CarInput.Disable();
    }

    private float GetSteeringInput()
    {
        Vector2 steer = controls.CarInput.Steering.ReadValue<Vector2>();
        steer.Normalize();
        return steer.x;
    }

    private float GetThrottleInput()
    {
        float rawThrottle = controls.CarInput.Accelerate.ReadValue<float>();
        return -rawThrottle;
    }

    private float GetBrakeInputRaw()
    {
        return controls.CarInput.Brake.ReadValue<float>();
    }

    void Update()
    {
        if (centerOfMassObject)
        {
            rb.centerOfMass = transform.InverseTransformPoint(centerOfMassObject.transform.position);
        }

        if (!isAutomatic)
        {
            if (controls.CarInput.ShiftUp.triggered) ShiftGear(1);
            if (controls.CarInput.ShiftDown.triggered) ShiftGear(-1);
        }

        UpdateWheelPoses();
    }

    void ShiftGear(int direction)
    {
        currentGear += direction;
        currentGear = Mathf.Clamp(currentGear, -1, gearRatios.Length);
    }

    void FixedUpdate()
    {
        float throttleInput = GetThrottleInput();
        float steeringInput = GetSteeringInput();
        float brakeInput = GetBrakeInputRaw();

        if (throttleInput == 0f && steeringInput == 0f && brakeInput == 0f && mobileInputController != null)
        {
            float mobileVertical = mobileInputController.GetVerticalInput();
            steeringInput = mobileInputController.GetHorizontalInput();

            if (mobileVertical > 0) throttleInput = -mobileVertical;
            else if (mobileVertical < 0) brakeInput = Mathf.Abs(mobileVertical);
        }

        float v = 0f;
        float currentBrakeTorque = 0f; // Unified brake torque variable
        float h = steeringInput * maxSteerAngle;

        float forwardVelocity = transform.InverseTransformDirection(rb.velocity).z;
        float currentSpeedKmph = rb.velocity.magnitude * 3.6f;

        // 1. Calculate Gear Ratio
        float gearRatio = 1f;
        if (currentGear > 0 && gearRatios.Length > 0)
        {
            gearRatio = gearRatios[Mathf.Clamp(currentGear - 1, 0, gearRatios.Length - 1)];
        }
        else if (currentGear == -1)
        {
            gearRatio = 1.5f; // Reverse gear ratio
        }
        else
        {
            gearRatio = 0f; // Neutral
        }

        // Get the physical RPM of the wheels
        float averageWheelRPM = Mathf.Abs((frontLeftWheel.rpm + frontRightWheel.rpm) / 2f);

        // Derive the RPM based strictly on how fast the car's body is moving forward
        float velocityBasedRPM = (Mathf.Abs(forwardVelocity) * 60f) / (2f * Mathf.PI * frontLeftWheel.radius);

        float effectiveWheelRPM = Mathf.Max(averageWheelRPM, velocityBasedRPM);
        float engineRPM = effectiveWheelRPM * gearRatio;

        // 2. TRANSMISSION LOGIC
        if (isAutomatic)
        {
            if (brakeInput > 0.1f)
            {
                if (forwardVelocity > 0.5f)
                {
                    currentBrakeTorque = brakeInput * brakeForce;
                }
                else
                {
                    v = brakeInput * (motorForce * 2f); // Reversing
                    currentGear = -1;
                }
            }
            else
            {
                v = throttleInput * motorForce;
                if (currentGear < 1) currentGear = 1;

                // Automatic Shifting
                if (currentGear > 0)
                {
                    if (engineRPM > shiftThreshold && currentGear < gearRatios.Length)
                    {
                        currentGear++; // Upshift
                    }
                    else if (engineRPM < downshiftThreshold && currentGear > 1)
                    {
                        currentGear--; // Downshift
                    }
                }
            }
        }
        else
        {
            // Manual Input
            if (brakeInput > 0.1f) currentBrakeTorque = brakeInput * brakeForce;

            if (currentGear == 0) // Neutral
            {
                v = 0f;
            }
            else if (currentGear == -1) // Reverse
            {
                v = -throttleInput * (motorForce * 2f);
            }
            else // Forward
            {
                v = throttleInput * motorForce;
            }
        }

        // 3. TRUE ENGINE BRAKING & REV LIMITER (Fixed)
        if (currentGear > 0)
        {
            if (engineRPM > shiftThreshold)
            {
                // STANDARD REV LIMITER: Always cut throttle if engine is at max RPM
                v = 0f;

                // MONEY SHIFT LOGIC: Check if the car's forward momentum is what is forcing the engine to over-rev.
                // This isolates aggressive downshifts from just accelerating into the redline.
                float overRevFromMomentum = (velocityBasedRPM * gearRatio) - shiftThreshold;

                if (overRevFromMomentum > 0f && brakeInput <= 0.1f)
                {
                    // Apply heavy braking ONLY when a downshift forces the engine past its limit
                    currentBrakeTorque = engineBrakingForce + (overRevFromMomentum * 1.5f);
                }
            }
            // (Removed the heavy coasting brake entirely so letting off the gas in 1st/2nd doesn't snap your neck)
        }

        // Apply unified brakes
        ApplyBrakeTorque(currentBrakeTorque);

        UpdateUI(currentSpeedKmph);

        // 4. Apply motor torque
        float adjustedTorque = v * gearRatio;
        frontLeftWheel.motorTorque = adjustedTorque;
        frontRightWheel.motorTorque = adjustedTorque;
        rearLeftWheel.motorTorque = enable4x4 ? adjustedTorque : 0f;
        rearRightWheel.motorTorque = enable4x4 ? adjustedTorque : 0f;

        // Apply steering
        frontLeftWheel.steerAngle = h;
        frontRightWheel.steerAngle = h;

        Quaternion currentRotation = frontLeftWheelTransform.rotation;
        float angularVelocity = Quaternion.Angle(prevRotation, currentRotation) / Time.fixedDeltaTime;
        prevRotation = currentRotation;

        bool isMoving = currentSpeedKmph > 0.5f;

        // Effects, Sound, and Particles
        bool shouldPlayDustParticles = (IsWheelSlipping(frontLeftWheel) || IsWheelDrifting(frontLeftWheel) || (IsWheelBraking(frontLeftWheel) && isMoving)) ||
                                       (IsWheelSlipping(frontRightWheel) || IsWheelDrifting(frontRightWheel) || (IsWheelBraking(frontRightWheel) && isMoving)) ||
                                       (IsWheelSlipping(rearLeftWheel) || IsWheelDrifting(rearLeftWheel) || (IsWheelBraking(rearLeftWheel) && isMoving)) ||
                                       (IsWheelSlipping(rearRightWheel) || IsWheelDrifting(rearRightWheel) || (IsWheelBraking(rearRightWheel) && isMoving));

        SetDustParticleSystemState(frontLeftDustParticleSystem, shouldPlayDustParticles);
        SetDustParticleSystemState(frontRightDustParticleSystem, shouldPlayDustParticles);
        SetDustParticleSystemState(rearLeftDustParticleSystem, shouldPlayDustParticles);
        SetDustParticleSystemState(rearRightDustParticleSystem, shouldPlayDustParticles);

        if (engineAudioSource != null)
        {
            float targetPitchByRPM = Mathf.Lerp(0.5f, 2.5f, engineRPM / shiftThreshold);

            if (currentGear == 0 && Mathf.Abs(throttleInput) > 0.1f)
            {
                targetPitchByRPM = Mathf.Lerp(targetPitchByRPM, 2f, Mathf.Abs(throttleInput));
            }

            if (engineRPM > shiftThreshold)
            {
                targetPitchByRPM = Mathf.Clamp(targetPitchByRPM, 2.5f, 3.5f);
            }

            engineAudioSource.pitch = Mathf.Lerp(engineAudioSource.pitch, targetPitchByRPM, Time.deltaTime * 5f);
        }

        if (!hasStartedMoving && currentSpeedKmph > 0.5f && engineStartAudioSource != null)
        {
            engineStartAudioSource.Play();
            hasStartedMoving = true;
        }

        if (currentSpeedKmph < 0.5f)
        {
            hasStartedMoving = false;
        }
    }

    void ApplyBrakeTorque(float force)
    {
        foreach (WheelCollider wheelCollider in wheelCollidersBrake)
        {
            wheelCollider.brakeTorque = force;
        }
    }

    void UpdateUI(float speed)
    {
        if (speedText != null)
        {
            speedText.text = Mathf.RoundToInt(speed) + " KM/H";
        }

        if (gearText != null)
        {
            if (currentGear == -1)
            {
                gearText.text = "R";
                gearText.color = Color.red;
            }
            else if (currentGear == 0)
            {
                gearText.text = "N";
                gearText.color = Color.gray;
            }
            else
            {
                gearText.text = (isAutomatic ? "D" : "") + currentGear.ToString();
                gearText.color = Color.white;
            }
        }
    }

    bool IsWheelSlipping(WheelCollider wheel)
    {
        WheelHit hit;
        return wheel.GetGroundHit(out hit) && hit.sidewaysSlip > 0.1f;
    }

    bool IsWheelDrifting(WheelCollider wheel)
    {
        WheelHit hit;
        return wheel.GetGroundHit(out hit) && hit.forwardSlip > 0.1f;
    }

    bool IsWheelBraking(WheelCollider wheel)
    {
        return wheel.isGrounded && Mathf.Abs(wheel.rpm) < 1f && wheel.brakeTorque > 0f;
    }

    void SetDustParticleSystemState(ParticleSystem dustParticleSystem, bool shouldPlay)
    {
        if (dustParticleSystem == null) return;
        if (shouldPlay && !dustParticleSystem.isPlaying) dustParticleSystem.Play();
        else if (!shouldPlay && dustParticleSystem.isPlaying) dustParticleSystem.Stop();
    }

    void UpdateWheelPoses()
    {
        UpdateWheelPose(frontLeftWheel, frontLeftWheelTransform);
        UpdateWheelPose(frontRightWheel, frontRightWheelTransform, true);
        UpdateWheelPose(rearLeftWheel, rearLeftWheelTransform);
        UpdateWheelPose(rearRightWheel, rearRightWheelTransform, true);
    }

    void UpdateWheelPose(WheelCollider collider, Transform wheelTransform, bool flip = false)
    {
        if (collider == null || wheelTransform == null) return;
        Vector3 pos = wheelTransform.position;
        Quaternion quat = wheelTransform.rotation;
        collider.GetWorldPose(out pos, out quat);
        if (flip) quat *= Quaternion.Euler(0, 180, 0);
        wheelTransform.position = pos;
        wheelTransform.rotation = quat;
    }
}