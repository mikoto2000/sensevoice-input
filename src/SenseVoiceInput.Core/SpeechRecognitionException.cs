namespace SenseVoiceInput.Core;
public enum RecognitionEngine { WhisperOnnx, SenseVoice }
public enum RecognitionError { ModelNotFound, ModelLoadFailed, CudaUnavailable, AudioPreprocessingFailed, InferenceFailed, TokenizerFailed, Cancelled }
public sealed class SpeechRecognitionException(RecognitionError code, string message, Exception? inner = null) : Exception($"{code}: {message}", inner)
{
    public RecognitionError Code { get; } = code;
}
