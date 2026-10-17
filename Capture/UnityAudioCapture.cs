using System;
using System.Threading;
using UnityEngine;

namespace UnityRuntimeCameraRecorder
{
    // Captures Unity's active audio mix and forwards raw samples to FFmpeg.
    internal sealed class UnityAudioCapture : MonoBehaviour
    {
        private Func<byte[], bool> _writeAudio;
        private int _activeCallbacks;
        private volatile int _channels;

        internal int Channels => _channels;
        internal int SampleRate => AudioSettings.outputSampleRate;
        internal bool IsReady => _channels > 0;

        // Assigns the format-neutral destination that receives captured audio buffers.
        internal void Initialize(Func<byte[], bool> writeAudio)
        {
            Volatile.Write(ref _writeAudio, writeAudio);
        }

        // Stops new submissions and waits for an in-flight callback before closing the pipe.
        internal void StopCapture()
        {
            Volatile.Write(ref _writeAudio, null);
            if (!SpinWait.SpinUntil(() => Volatile.Read(ref _activeCallbacks) == 0, 1000))
            {
                throw new TimeoutException("An audio callback did not finish before recorder shutdown.");
            }
        }

        // Receives Unity audio samples on the audio thread and queues them for FFmpeg.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            Interlocked.Increment(ref _activeCallbacks);
            try
            {
                _channels = channels;
                Func<byte[], bool> writeAudio = Volatile.Read(ref _writeAudio);
                if (writeAudio == null)
                {
                    return;
                }

                byte[] bytes = new byte[data.Length * sizeof(float)];
                Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
                writeAudio(bytes);
            }
            catch (Exception exception)
            {
                RecorderLog.WriteError(exception);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCallbacks);
            }
        }
    }
}
