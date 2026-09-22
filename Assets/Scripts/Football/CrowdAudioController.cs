using UnityEngine;
using System.Collections;

public class CrowdAudioController : MonoBehaviour
{
    public AudioSource crowdAudioSource;
    public float baseVolume = 0.3f;
    public float goalVolume = 1.0f;
    public float cheerDuration = 4.0f;

    private Coroutine cheerCoroutine;

    void Start()
    {
        if (crowdAudioSource != null)
        {
            crowdAudioSource.loop = true;
            crowdAudioSource.volume = baseVolume;
            if (!crowdAudioSource.isPlaying)
            {
                crowdAudioSource.Play();
            }
        }
    }

    public void TriggerGoalCheer()
    {
        if (crowdAudioSource == null) return;
        
        if (cheerCoroutine != null)
            StopCoroutine(cheerCoroutine);
            
        cheerCoroutine = StartCoroutine(CheerRoutine());
    }

    private IEnumerator CheerRoutine()
    {
        float t = 0;
        // Ramp up
        while (t < 0.5f)
        {
            t += Time.deltaTime;
            crowdAudioSource.volume = Mathf.Lerp(baseVolume, goalVolume, t / 0.5f);
            yield return null;
        }

        crowdAudioSource.volume = goalVolume;
        yield return new WaitForSeconds(cheerDuration);

        // Ramp down
        t = 0;
        while (t < 2f)
        {
            t += Time.deltaTime;
            crowdAudioSource.volume = Mathf.Lerp(goalVolume, baseVolume, t / 2f);
            yield return null;
        }
        
        crowdAudioSource.volume = baseVolume;
    }
}
