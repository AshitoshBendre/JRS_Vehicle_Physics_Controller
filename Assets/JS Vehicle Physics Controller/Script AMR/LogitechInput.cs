using UnityEngine;
using UnityEngine.InputSystem;

public class LogitechInput : MonoBehaviour
{
    public InputActionAsset actions;

    private InputAction steering;
    private InputAction accelerator;
    private InputAction brake;

    public float Steering => steering.ReadValue<float>();
    public float Accelerator => accelerator.ReadValue<float>();
    public float Brake => brake.ReadValue<float>();

    void Awake()
    {
        var map = actions.FindActionMap("Car Input");

        steering = map.FindAction("Steering");
        accelerator = map.FindAction("Accelerate");
        brake = map.FindAction("Brake");
    }

    void OnEnable()
    {
        steering.Enable();
        accelerator.Enable();
        brake.Enable();
    }

    void OnDisable()
    {
        steering.Disable();
        accelerator.Disable();
        brake.Disable();
    }
}