# Third-party offline voice components

Madi AI uses local speech synthesis only. No voice input or generated speech is sent to a paid cloud API.

## Piper (optional bundled Windows binary)
- Source: https://github.com/rhasspy/piper
- Release: https://github.com/rhasspy/piper/releases/tag/2023.11.14-2
- Original Piper project license: MIT. The actively maintained successor is a separate GPL-licensed project; this installer uses the archived MIT Windows binary.
- The Piper binary and its dependencies remain third-party software.

## English voice: en_US-ljspeech-medium
- Source: https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_US/ljspeech/medium
- Dataset: LJ Speech, public-domain recordings.
- Voice trained by Piper contributors. Preserve upstream MODEL_CARD for exact attribution.

## Urdu voice: ur_PK-aegis_female-medium
- Source: https://huggingface.co/rhasspy/piper-voices/tree/main/ur/ur_PK/aegis_female/medium
- Model card states MIT. Voice contributed by mahwizzzz; derived from earlier Fasih Urdu work.
- Preserve the upstream MODEL_CARD.

## Speech recognition
- Whisper.net runtime: https://github.com/sandrohanea/whisper.net
- Whisper tiny multilingual model: https://huggingface.co/ggerganov/whisper.cpp

These third-party components are independent of the Madi AI application. The original licenses, model cards and upstream attribution govern redistribution. Speech quality, multilingual accuracy and runtime speed vary with installed hardware and model choice.
