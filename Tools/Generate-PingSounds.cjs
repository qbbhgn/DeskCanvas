const fs = require('node:fs')
const path = require('node:path')

const output = path.join(__dirname, '../Wallpapers/PingWheel/assets/sounds')
const sampleRate = 44100
const signals = {
  danger: [880, 660, 440],
  push: [392, 523.25, 783.99],
  'on-my-way': [523.25, 659.25, 1046.5],
  'all-in': [220, 440, 880],
  assist: [659.25, 783.99, 659.25],
  vision: [1046.5, 1318.51, 1567.98],
  missing: [783.99, 587.33, 783.99],
  'enemy-vision': [622.25, 440, 622.25],
}

fs.mkdirSync(output, { recursive: true })
for (const [name, notes] of Object.entries(signals)) {
  const duration = 0.72
  const samples = Math.ceil(sampleRate * duration)
  const buffer = Buffer.alloc(44 + samples * 2)
  buffer.write('RIFF', 0)
  buffer.writeUInt32LE(buffer.length - 8, 4)
  buffer.write('WAVEfmt ', 8)
  buffer.writeUInt32LE(16, 16)
  buffer.writeUInt16LE(1, 20)
  buffer.writeUInt16LE(1, 22)
  buffer.writeUInt32LE(sampleRate, 24)
  buffer.writeUInt32LE(sampleRate * 2, 28)
  buffer.writeUInt16LE(2, 32)
  buffer.writeUInt16LE(16, 34)
  buffer.write('data', 36)
  buffer.writeUInt32LE(samples * 2, 40)
  for (let sampleIndex = 0; sampleIndex < samples; sampleIndex++) {
    const time = sampleIndex / sampleRate
    let value = 0
    notes.forEach((frequency, noteIndex) => {
      const elapsed = time - noteIndex * 0.105
      if (elapsed < 0) return
      const envelope = Math.min(elapsed / 0.006, 1) * Math.exp(-elapsed * 10)
      const phase = 2 * Math.PI * frequency * elapsed
      value +=
        envelope * (Math.sin(phase) + 0.22 * Math.sin(phase * 2.003)) * 0.22
    })
    const fade = Math.min(1, (duration - time) / 0.04)
    buffer.writeInt16LE(
      Math.round(Math.max(-1, Math.min(1, value * fade)) * 32767),
      44 + sampleIndex * 2
    )
  }
  fs.writeFileSync(path.join(output, `${name}.wav`), buffer)
}
console.log(`Generated ${Object.keys(signals).length} signal sounds: ${output}`)
