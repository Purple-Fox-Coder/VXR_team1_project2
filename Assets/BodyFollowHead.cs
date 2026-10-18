using UnityEngine;

public class BodyFollowHead : MonoBehaviour
{
	public Transform head;
	
	public float yaw_smoothing = 8F;
	public float yaw_offset = 0F;
	public float y_trim = 0.5F;
	public float model_height = 1.8F;

	void Awake()
	{
		if (this.head == null && Camera.main != null)
			this.head = Camera.main.transform;
	}

	void LateUpdate()
	{
		Vector3 head_pos = this.head.position;
		// head_pos.y = this.transform.parent.position.y;
		head_pos.y = this.head.position.y - (this.model_height * 0.5F) + this.y_trim;
		this.transform.position = head_pos;

		Quaternion target = Quaternion.Euler(0F, this.head.eulerAngles.y + this.yaw_offset, 0F);
		this.transform.rotation = Quaternion.Slerp(
			this.transform.rotation, target,
			1F - Mathf.Exp(-this.yaw_smoothing * Time.deltaTime)	
		);
	}
}
