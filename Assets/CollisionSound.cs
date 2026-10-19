using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class CollisionSound : MonoBehaviour
{
	[Serializable]
	public class LayerImpactSound
	{
		[Tooltip("Layers of the thing we hit that use this sound")]
		public LayerMask layers;
		[Tooltip("Pick random per impact")]
		public AudioClip[] clips;
	}

	[Header("Sounds")]
	[Tooltip("Sound per layer of other object.  First entry containing layer wins")]
	public List<LayerImpactSound> layer_sounds = new List<LayerImpactSound>();
	[Tooltip("Played when the other object's layer has no sound defined above")]
	public AudioClip[] default_clips;

	[Header("Impact")]
	[Tooltip("Relative speed (m/s) below which nothing plays")]
	[Min(0F)]
	public float min_impact_speed = 0.5F;
	[Tooltip("Relative speed (m/s) at which the sound is at max vol")]
	[Min(0.01F)]
	public float max_impact_speed = 8F;
	[Range(0F, 1F)]
	public float min_volume = 0.1F;
	[Range(0F, 1F)]
	public float max_volume = 1F;

	[Header("Variation")]
	[Tooltip("Random pitch range to avoid identicality")]
	public Vector2 pitch_range = new Vector2(0.9F, 1.1F);
	[Tooltip("Minimum seconds between sounds to avoid rattling")]
	[Min(0F)]
	public float cooldown = 0.08F;

	private AudioSource source;
	private readonly Dictionary<int, LayerImpactSound> sound_lookup = new Dictionary<int, LayerImpactSound>();
	private float last_play_time = float.NegativeInfinity;

	private void BuildSoundLookup()
	{
		this.sound_lookup.Clear();

		foreach (LayerImpactSound entry in this.layer_sounds)
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

	private AudioClip PickClip(int layer)
	{
		if (this.sound_lookup.TryGetValue(layer, out LayerImpactSound entry) && entry.clips != null && entry.clips.Length > 0)
		{
			AudioClip picked = entry.clips[UnityEngine.Random.Range(0, entry.clips.Length)];
			if (picked != null)
				return picked;
		}

		if (this.default_clips != null && this.default_clips.Length > 0)
			return this.default_clips[UnityEngine.Random.Range(0, this.default_clips.Length)];

		return null;
	}

	void Awake()
	{
		this.source = GetComponent<AudioSource>();
		this.source.playOnAwake = false;
		this.source.spatialBlend = 1F;

		this.BuildSoundLookup();
	}

	void OnCollisionEnter(Collision collision)
	{
		if (Time.time - this.last_play_time < this.cooldown)
			return;

		float speed = collision.relativeVelocity.magnitude;
		if (speed < this.min_impact_speed)
			return;

		AudioClip clip = this.PickClip(collision.gameObject.layer);
		if (clip == null)
			return;

		float t = Mathf.InverseLerp(this.min_impact_speed, this.max_impact_speed, speed);
		float volume = Mathf.Lerp(this.min_volume, this.max_volume, t);

		this.source.pitch = UnityEngine.Random.Range(this.pitch_range.x, this.pitch_range.y);
		this.source.PlayOneShot(clip, volume);
		this.last_play_time = Time.time;
	}
}
