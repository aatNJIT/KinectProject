extends TextureButton

const save_path = "res://Sounds/mic.wav";

var effect  # See AudioEffect in docs
var recording  # See AudioStreamSample in docs

var stereo := true
var mix_rate := 16000  # This is the default mix rate on recordings
var format := 1  # This equals to the default format: 16 bits

func _ready():
	effect = AudioServer.get_bus_effect(1, 0)

func _on_pressed() -> void:
	if effect.is_recording_active():
		recording = effect.get_recording()
		effect.set_recording_active(false)
		recording.set_mix_rate(mix_rate)
		recording.set_format(format)
		recording.set_stereo(stereo)
		recording.set_loop_mode(1)
		recording.save_to_wav(save_path)
		print("Status: Saved WAV file to: %s\n(%s)" % [save_path, ProjectSettings.globalize_path(save_path)])
		var controller = get_node("/root/UiTest")
		if controller and controller.has_method("OnRecordingEnd"):
			controller.call("OnRecordingEnd")
	else:
		effect.set_recording_active(true)
		print("Status: Recording...")
		var controller = get_node("/root/UiTest")
		if controller and controller.has_method("OnRecordingEnd"):
			controller.call("OnRecordingStart")
