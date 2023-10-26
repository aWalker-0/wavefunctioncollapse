using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour {

	[Range(1f, 5f)]
	public float MovementSpeed = 1f;

	[Range(1, 500f)]
	public float LookSensitivity = 200f;

	[Range(1, 500f)]
	public float MouseSensitivity = 3;

	[Range(1, 100f)]
	public float JumpStrength = 2f;

	private CharacterController characterController;
	private Transform cameraTransform;

	private float cameraTilt = 0f;
	private float verticalSpeed = 0f;
	private float timeInAir = 0f;
	private bool jumpLocked = false;

	public LayerMask CollisionLayers;
	
	void OnEnable() {
		this.characterController = this.GetComponent<CharacterController>();
		this.cameraTransform = this.GetComponentInChildren<Camera>().transform;
		this.cameraTilt = this.cameraTransform.localRotation.eulerAngles.x;
	}
	
	void Update() {
		// Check if the character is touching the ground
	    bool touchesGround = this.onGround();

	    // Determine the run multiplier based on the "Run" input axis
	    float runMultiplier = 1f + 2f * Input.GetAxis("Run");

	    // Store the initial y position of the character
	    float y = this.transform.position.y;

	    // Calculate the movement vector based on input axes "Move Y" and "Move X"
	    Vector3 movementVector = this.transform.forward * Input.GetAxis("Move Y") + this.transform.right * Input.GetAxis("Move X");

	    // Normalize the movement vector to prevent faster diagonal movement
	    if (movementVector.sqrMagnitude > 1) {
	        movementVector.Normalize();
	    }

	    // Move the character based on the calculated movement vector, speed, and run multiplier
	    // ? We use "Time.delta" here to ensure that the character moves at a constant speed, regardless of the 
	    // ?  framerate.
	    this.characterController.Move(movementVector * Time.deltaTime * this.MovementSpeed * runMultiplier);

	    // Calculate the vertical movement of the character after applying the movement vector
	    float verticalMovement = this.transform.position.y - y;

	    // Adjust the character's position if it moved downward
	    if (verticalMovement < 0) {
	        this.transform.position += Vector3.down * verticalMovement;
	    }

	    // Rotate the character based on the "Mouse Look X" and "Look X" input axes
	    this.transform.localRotation = Quaternion.AngleAxis(Input.GetAxis("Mouse Look X") * this.MouseSensitivity + Input.GetAxis("Look X") * this.LookSensitivity * Time.deltaTime, Vector3.up) * this.transform.rotation;

	    // Adjust the camera tilt based on the "Mouse Look Y" and "Look Y" input axes, clamping it between -90 and 90 degrees
	    this.cameraTilt = Mathf.Clamp(this.cameraTilt - Input.GetAxis("Mouse Look Y") * this.MouseSensitivity - Input.GetAxis("Look Y") * this.LookSensitivity * Time.deltaTime, -90f, 90f);

	    // Apply the camera tilt to the camera's local rotation
	    this.cameraTransform.localRotation = Quaternion.AngleAxis(this.cameraTilt, Vector3.right);

	    // Update the timeInAir variable based on whether the character is touching the ground or not
	    if (touchesGround) {
	        this.timeInAir = 0;
	    } else {
	        this.timeInAir += Time.deltaTime;
	    }

	    // Reset the vertical speed if the character is touching the ground and moving downward
	    if (touchesGround && this.verticalSpeed < 0) {
	        this.verticalSpeed = 0;
	    } else {
	        // Apply gravity to the vertical speed
	        this.verticalSpeed -= 9.18f * Time.deltaTime;
	    }

	    // Check if the "Jump" input axis is released and unlock the jump
	    if (Input.GetAxisRaw("Jump") < 0.1f) {
	        this.jumpLocked = false;
	    }

	    // Handle jumping if the jump is not locked, the character has been in the air for less than 0.5 seconds, and the "Jump" input axis has a value greater than 0.1
	    if (!this.jumpLocked && this.timeInAir < 0.5f && Input.GetAxisRaw("Jump") > 0.1f) {
	        this.timeInAir = 0.5f;
	        this.verticalSpeed = this.JumpStrength;
	        this.jumpLocked = true;
	    }

	    // Apply the jetpack functionality if the "Jetpack" input axis has a value greater than 0.1
	    if (Input.GetAxisRaw("Jetpack") > 0.1f) {
	        this.verticalSpeed = 2f;
	    }

	    // Move the character vertically based on the calculated vertical speed
	    this.characterController.Move(Vector3.up * Time.deltaTime * this.verticalSpeed);
	}


	public void Enable() {
		this.verticalSpeed = 0;
	}

	private bool onGround() {
		// Create a Ray("starting point", "the direction to point the ray")
		var ray = new Ray(this.transform.position, Vector3.down);
		// Cast the ray with a sphere on the end of it (which the sphere collides with anything, instead of a small point, that a normal ray would normally cast)
		return Physics.SphereCast(ray, this.characterController.radius, this.characterController.height / 2 - this.characterController.radius + 0.1f, this.CollisionLayers);
	}
}
