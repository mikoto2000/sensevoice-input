# Certification notes — draft

Before submission: remove this heading and the release-owner checklist below. Use the text only after validating the final MSIX. Do not claim tests that have not been performed.

## App purpose and permissions

SenseVoice Input is a Windows 11 x64 desktop dictation utility for Japanese. It uses local Whisper ONNX inference and Silero VAD. No user account or paid service credentials are needed.

The app requests `runFullTrust` because it is a WPF desktop application using WASAPI microphone capture, global keyboard triggers, a notification-area icon, and Win32 text injection into the user-selected foreground application. It also offers an optional clipboard paste mode. It does not need elevation, install a driver, or record ordinary keystrokes to a log. Recognition audio and transcription are not uploaded by the app.

## First-run requirements

A microphone and Internet access are needed for initial setup. Approximately 1.62 GB of Whisper model data plus Silero VAD and supporting files are downloaded from Hugging Face/GitHub/CDNs. Wait for setup to complete; progress, cancellation and retry are available in the settings window. Inference can run offline afterwards. The Store build includes the .NET runtime.

CPU is the default. An NVIDIA GPU is not required. Existing saved settings are preserved. For optional CUDA testing, use a CUDA 13 compatible NVIDIA GPU and driver. The app checks the driver and, if runtime files are missing, requests consent to NVIDIA's terms before downloading them. It does not download/install the NVIDIA driver. It does not silently switch a saved CUDA selection to CPU on failure.

## Test steps

1. Install on a clean Windows 11 x64 test account. Launch from Start. The first-run settings window should show CPU/CUDA guidance.
2. Allow the initial model download to finish. Select a microphone and CPU, then save. Configure a PTT trigger if the default F12 conflicts with the test environment.
3. Open Notepad as a standard user, focus an empty document, hold the configured trigger and say 「こんにちは。音声入力のテストです。」 Release the key and allow time for the first model load and recognition. CPU processing may be slow.
4. Confirm recognized Japanese text is entered. Small transcription differences may occur. PTT recordings are limited to 60 seconds.
5. Optionally enable automatic voice input in settings and save, focus an editable field, and use the configured toggle trigger. Speak, then pause for the silence timeout. Toggle it off afterwards. Automatic segments are limited to 30 seconds.
6. Close the settings window. The app continues in the notification area. Double-click its icon to reopen settings; select Exit from the icon menu to terminate it.
7. Optional clipboard mode: use non-sensitive clipboard test data, select the paste mode, save, then repeat dictation and check clipboard restoration.

## Known functional boundaries

Recognition is Japanese only. Elevated applications, UAC screens and applications with custom text-input handling may reject text injection. The app cancels pending automatic input when the target changes. It does not provide real-time partial transcription or translation. Automatic input starts off after restart. Text should be reviewed before use.

## Release-owner checklist — do not submit this section

- Verify every step against the uploaded package and record CPU/GPU hardware and timings.
- Complete the packaged startup-task integration and document how to test it here.
- Confirm third-party download availability, privacy URL, and support URL.
- Supply any additional answers Partner Center requests about `runFullTrust` or other declarations.
