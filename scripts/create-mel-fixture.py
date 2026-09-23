import importlib.util,json,numpy as np
spec=importlib.util.spec_from_file_location('reference','artifacts/reference_audio_utils.py')
r=importlib.util.module_from_spec(spec); spec.loader.exec_module(r)
x=np.zeros(480000,dtype=np.float32)
# Deterministic integer waveform, plus edge impulse exercises reflect padding.
x[:16000]=np.array([((i*37)%2001-1000)/2000 for i in range(16000)],dtype=np.float32)
x[0]=0.9
mel=r.mel_filter_bank(201,128,0,8000,16000,norm='slaney',mel_scale='slaney')
s=r.spectrogram(x,r.window_function(400,'hann'),400,160,power=2.0,mel_filters=mel,log_mel='log10')[:,:-1]
s=(np.maximum(s,s.max()-8)+4)/4
indexes=[(m,t) for m in [0,1,10,32,64,100,127] for t in [0,1,2,50,99,100,101,200,2999]]
json.dump([{'mel':m,'frame':t,'value':float(s[m,t])} for m,t in indexes],open('tests/SenseVoiceInput.Windows.Tests/Fixtures/whisper-mel-reference.json','w'),indent=2)
