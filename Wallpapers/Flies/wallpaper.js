;(() => {
  'use strict'
  const canvas = document.getElementById('scene')
  const context = canvas.getContext('2d')
  const world = new FlyWorld()
  const pointer = { x: -100, y: -100, mode: '', pulse: 0 }
  let paused = false,
    previous = 0
  function resize() {
    world.resize(innerWidth, innerHeight)
    const ratio = Math.min(devicePixelRatio || 1, 2)
    canvas.width = Math.round(innerWidth * ratio)
    canvas.height = Math.round(innerHeight * ratio)
    context.setTransform(ratio, 0, 0, ratio, 0, 0)
  }
  function ellipse(x, y, rx, ry, color, angle = 0) {
    context.beginPath()
    context.ellipse(x, y, rx, ry, angle, 0, Math.PI * 2)
    context.fillStyle = color
    context.fill()
  }
  function line(points, color, width = 1) {
    context.beginPath()
    context.moveTo(points[0], points[1])
    for (let index = 2; index < points.length; index += 2)
      context.lineTo(points[index], points[index + 1])
    context.strokeStyle = color
    context.lineWidth = width
    context.stroke()
  }
  function insect(fly, dead = false) {
    context.save()
    context.translate(fly.x, fly.y)
    context.rotate(fly.angle)
    context.scale(world.options.flySize, world.options.flySize)
    const flight = !dead && fly.state === 'fly'
    const wiggle = Math.sin(fly.phase || 0)
    if (flight) {
      context.shadowColor = '#0005'
      context.shadowBlur = 3
      context.shadowOffsetY = 4
    } else ellipse(1, 2, dead ? 10 : 7, 10, '#18312620')
    if (dead) {
      ellipse(0, 2, 8, 4, '#273930')
      ellipse(-4, -3, 5, 3, '#42554a')
      ellipse(-10, 7, 4, 7, '#cad7cebb', -0.6)
      if (fly.variant !== 1) ellipse(9, 3, 4, 8, '#cad7ce99', 0.8)
      line([-7, -2, -13, -4, -15, 0], '#252b27')
      line([3, -3, 8, -8, 12, -7], '#252b27')
      ellipse(-3, -4, 2, 1.5, '#7b3731')
    } else {
      for (const side of [-1, 1]) {
        for (let leg = 0; leg < 3; leg++) {
          const grooming =
            (fly.state === 'hands' || fly.state === 'eyes') && leg === 0
          const endX = grooming
            ? side * (2 + wiggle * 2)
            : side * (10 + (fly.state === 'walk' ? wiggle * 2 : 0))
          const endY = grooming
            ? fly.state === 'eyes'
              ? -12
              : -17
            : -8 + leg * 8
          line(
            [side * 3, -4 + leg * 4, side * 7, -8 + leg * 7, endX, endY],
            '#26312a',
            1.2
          )
        }
      }
      ellipse(0, 5, 4, 7 + (fly.state === 'lay' ? wiggle : 0), '#23362d')
      line([-3, 5, 3, 5], '#748c74')
      line([-3, 8, 3, 8], '#748c74')
      const spread = flight ? 0.7 + Math.sin(fly.phase * 5) * 0.45 : 0.26
      for (const side of [-1, 1]) {
        ellipse(side * (flight ? 8 : 4), 2, 4, 10, '#d1ded9bf', side * spread)
        line([side * 2, -4, side * (flight ? 12 : 6), 9], '#788d8955', 0.6)
      }
      ellipse(0, -3, 4.5, 5, '#38463c')
      line([-1, -7, -1, 0], '#a1ada4', 0.7)
      ellipse(0, -10, 4.5, 3.5, '#3a4237')
      ellipse(-3, -10, 2.2, 3, fly.hp === 1 ? '#b95739' : '#923d35')
      ellipse(3, -10, 2.2, 3, fly.hp === 1 ? '#b95739' : '#923d35')
      if (fly.stunned > 0) {
        context.strokeStyle = '#c56b2b'
        context.beginPath()
        context.arc(0, 0, 17, 0, Math.PI * 2)
        context.stroke()
      }
    }
    context.restore()
  }
  function tool() {
    if (!pointer.mode || window.deskCanvas?.nativeTools) return
    context.save()
    context.translate(pointer.x, pointer.y)
    context.rotate(
      pointer.pulse > 0 ? -0.6 * Math.sin(pointer.pulse * Math.PI) : -0.25
    )
    if (pointer.mode === 'swat') {
      context.fillStyle = '#dc7147'
      context.fillRect(-2, 6, 5, 43)
      context.fillStyle = '#edf6f1'
      context.fillRect(-18, -25, 36, 32)
      context.strokeStyle = '#386953'
      context.lineWidth = 2
      context.strokeRect(-18, -25, 36, 32)
      for (let offset = -12; offset < 18; offset += 6)
        line([offset, -25, offset, 7], '#386953', 1)
      for (let offset = -19; offset < 7; offset += 6)
        line([-18, offset, 18, offset], '#386953', 1)
    } else {
      line([0, 0, 20, -42], '#866542', 6)
      for (let offset = -18; offset <= 18; offset += 3)
        line([0, -5, offset, 23], '#c8a34d', 3)
      line([-7, 3, 7, 3], '#327a69', 4)
    }
    context.restore()
  }
  function frame(timestamp) {
    const delta = previous ? Math.min((timestamp - previous) / 1000, 0.1) : 0
    previous = timestamp
    if (!paused) {
      world.step(delta)
      pointer.pulse = Math.max(0, pointer.pulse - delta * 4)
      context.clearRect(0, 0, innerWidth, innerHeight)
      for (const egg of world.eggs) {
        const ready = egg.age >= 30
        context.save()
        context.translate(egg.x, egg.y)
        context.rotate(
          egg.phase +
            (ready && world.flies.length < world.options.maxFlies
              ? Math.sin(world.time * 2) * 0.08
              : 0)
        )
        ellipse(1, 2, 3.5, 6, '#203a3025')
        ellipse(0, 0, 2.6, 5, egg.hits ? '#c7ba87' : '#fff9dc')
        line([-1, -3, -1, 2], '#c1b686', 0.7)
        context.restore()
      }
      for (const corpse of world.corpses) {
        context.globalAlpha = corpse.hits ? 0.45 : 0.85
        insect(corpse, true)
      }
      context.globalAlpha = 1
      for (const fly of world.flies) insect(fly)
      if (pointer.pulse > 0) {
        context.globalAlpha = pointer.pulse * 0.45
        context.strokeStyle = pointer.mode === 'swat' ? '#cb6941' : '#32806a'
        context.lineWidth = 2
        context.beginPath()
        context.arc(
          pointer.x,
          pointer.y,
          (pointer.mode === 'swat' ? 32 * world.options.flySize : 42) *
            (1.3 - pointer.pulse * 0.3),
          0,
          Math.PI * 2
        )
        context.stroke()
        context.globalAlpha = 1
      }
      tool()
    }
    requestAnimationFrame(frame)
  }
  function mode(alt, ctrl) {
    pointer.mode = ctrl ? 'sweep' : alt ? 'swat' : ''
    canvas.style.cursor = pointer.mode ? 'none' : 'default'
  }
  function mouse(event) {
    pointer.x = event.clientX
    pointer.y = event.clientY
    mode(event.altKey, event.ctrlKey)
    if (
      event.type !== 'mousedown' ||
      event.button !== 0 ||
      paused ||
      !pointer.mode
    )
      return
    event.preventDefault()
    pointer.pulse = 1
    if (pointer.mode === 'swat') world.strike(pointer.x, pointer.y)
    else world.sweep(pointer.x, pointer.y)
  }
  function settings(runtime) {
    world.configure(runtime.settings || {})
  }
  function desktop(detail) {
    mode(detail.altKey, detail.ctrlKey)
    pointer.x = detail.xRatio * innerWidth
    pointer.y = detail.yRatio * innerHeight
    world.icons = (detail.icons || []).map((icon) => ({
      x: icon.x * innerWidth,
      y: icon.y * innerHeight,
      width: icon.width * innerWidth,
      height: icon.height * innerHeight,
    }))
    window.deskCanvas.nativeTools = true
  }
  window.addEventListener('resize', resize)
  window.addEventListener('mousemove', mouse)
  window.addEventListener('mousedown', mouse)
  window.addEventListener('keydown', (event) =>
    mode(event.altKey, event.ctrlKey)
  )
  window.addEventListener('keyup', (event) => mode(event.altKey, event.ctrlKey))
  window.addEventListener('blur', () => mode(false, false))
  window.addEventListener('deskcanvas:ready', (event) => settings(event.detail))
  window.addEventListener('deskcanvas:property', (event) =>
    world.configure({ [event.detail.key]: event.detail.value })
  )
  window.addEventListener('deskcanvas:desktop', (event) =>
    desktop(event.detail)
  )
  window.addEventListener('deskcanvas:pause', () => {
    paused = true
    mode(false, false)
  })
  window.addEventListener('deskcanvas:resume', () => {
    paused = false
    previous = 0
  })
  window.flyWallpaper = { world, pointer }
  resize()
  for (let index = 0; index < 3; index++) {
    world.addFly(
      world.range(20, world.width - 20),
      world.range(20, world.height - 20)
    )
  }
  if (window.deskCanvas) settings(window.deskCanvas)
  requestAnimationFrame(frame)
})()
