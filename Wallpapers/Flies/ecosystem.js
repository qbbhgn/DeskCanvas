;(function (root) {
  'use strict'
  class FlyWorld {
    constructor(random = Math.random) {
      this.random = random
      this.width = 1280
      this.height = 720
      this.flies = []
      this.eggs = []
      this.corpses = []
      this.icons = []
      this.time = 0
      this.birthClock = 0
      this.options = {
        maxFlies: 30,
        spawnMinutes: 5,
        flySize: 1,
        background: '#e6ece8',
      }
    }
    range(min, max) {
      return min + this.random() * (max - min)
    }
    configure(values) {
      for (const key of Object.keys(this.options)) {
        if (values[key] !== undefined) this.options[key] = values[key]
      }
      this.options.maxFlies = Math.round(
        Math.max(1, Math.min(60, Number(this.options.maxFlies) || 30))
      )
      this.options.spawnMinutes = Math.max(
        1,
        Math.min(30, Number(this.options.spawnMinutes) || 5)
      )
      this.options.flySize = Math.max(
        0.7,
        Math.min(2, Number(this.options.flySize) || 1)
      )
      this.flies.length = Math.min(this.flies.length, this.options.maxFlies)
    }
    resize(width, height) {
      this.width = Math.max(64, width)
      this.height = Math.max(64, height)
      for (const item of [...this.flies, ...this.eggs, ...this.corpses]) {
        if (!item.entering) this.bound(item)
      }
    }
    bound(item) {
      item.x = Math.max(16, Math.min(this.width - 16, item.x))
      item.y = Math.max(16, Math.min(this.height - 16, item.y))
    }
    target(fly) {
      const icon =
        this.icons.length && fly.homebody
          ? this.icons[Math.floor(this.random() * this.icons.length)]
          : null
      fly.tx = icon
        ? icon.x + this.range(-12, icon.width + 12)
        : this.range(20, this.width - 20)
      fly.ty = icon
        ? icon.y + this.range(-10, icon.height + 10)
        : this.range(20, this.height - 20)
      fly.tx = Math.max(16, Math.min(this.width - 16, fly.tx))
      fly.ty = Math.max(16, Math.min(this.height - 16, fly.ty))
    }
    addFly(x, y, entering = false) {
      if (this.flies.length >= this.options.maxFlies) return
      const fly = {
        x,
        y,
        entering,
        hp: 2,
        homebody: this.random() < 0.82,
        state: 'fly',
        timer: 0,
        angle: 0,
        phase: this.range(0, 6.28),
        layAt: this.time + this.range(35, 65),
        stunned: 0,
      }
      this.target(fly)
      this.flies.push(fly)
    }
    immigrate() {
      const edge = Math.floor(this.random() * 4)
      this.addFly(
        edge === 0
          ? -24
          : edge === 1
            ? this.width + 24
            : this.range(20, this.width - 20),
        edge === 2
          ? -24
          : edge === 3
            ? this.height + 24
            : this.range(20, this.height - 20),
        true
      )
    }
    lay(fly) {
      if (this.eggs.length >= 90) return
      const egg = {
        x: fly.x + this.range(-6, 6),
        y: fly.y + this.range(-6, 6),
        hits: 0,
        age: 0,
        phase: this.range(0, 6.28),
      }
      this.bound(egg)
      this.eggs.push(egg)
    }
    step(seconds) {
      this.time += seconds
      this.birthClock += seconds
      for (const egg of this.eggs) egg.age += seconds
      const interval = this.options.spawnMinutes * 60
      if (this.birthClock >= interval) {
        this.birthClock %= interval
        if (this.flies.length < this.options.maxFlies) {
          const index = this.eggs.findIndex((egg) => egg.age >= 30)
          if (index >= 0) {
            const egg = this.eggs.splice(index, 1)[0]
            this.addFly(egg.x, egg.y)
          } else if (!this.flies.length && !this.eggs.length) this.immigrate()
        }
      }
      for (const fly of this.flies) {
        fly.phase += seconds * 12
        if (fly.stunned > 0) {
          fly.stunned -= seconds
          continue
        }
        if (fly.state === 'fly' || fly.state === 'walk') {
          const dx = fly.tx - fly.x,
            dy = fly.ty - fly.y
          const distance = Math.hypot(dx, dy)
          const speed =
            (fly.state === 'fly' ? 110 : 13) * (fly.hp === 1 ? 0.65 : 1)
          fly.angle = Math.atan2(dy, dx) + Math.PI / 2
          if (distance <= speed * seconds + 1) {
            fly.x = fly.tx
            fly.y = fly.ty
            fly.entering = false
            const choice = this.random()
            fly.state =
              this.time >= fly.layAt && this.eggs.length < 90
                ? 'lay'
                : choice < 0.35
                  ? 'hands'
                  : choice < 0.65
                    ? 'eyes'
                    : 'rest'
            fly.timer = this.range(3, 12)
          } else {
            fly.x += (dx / distance) * speed * seconds
            fly.y += (dy / distance) * speed * seconds
          }
        } else {
          fly.timer -= seconds
          if (fly.timer <= 0) {
            if (fly.state === 'lay') {
              this.lay(fly)
              fly.layAt = this.time + this.range(100, 240)
            }
            if (this.random() < 0.45) {
              fly.tx = Math.max(
                16,
                Math.min(this.width - 16, fly.x + this.range(-30, 30))
              )
              fly.ty = Math.max(
                16,
                Math.min(this.height - 16, fly.y + this.range(-30, 30))
              )
              fly.state = 'walk'
            } else {
              this.target(fly)
              fly.state = 'fly'
            }
          }
        }
        if (!fly.entering) this.bound(fly)
      }
    }
    strike(x, y) {
      let count = 0
      this.flies = this.flies.filter((fly) => {
        if (Math.hypot(fly.x - x, fly.y - y) > 32 * this.options.flySize)
          return true
        count++
        fly.hp--
        fly.stunned = 0.8
        if (fly.hp > 0) return true
        this.corpses.push({
          x: fly.x,
          y: fly.y,
          angle: fly.angle,
          hits: 0,
          variant: Math.floor(this.random() * 3),
        })
        if (this.corpses.length > 60) this.corpses.shift()
        return false
      })
      return count
    }
    sweep(x, y) {
      let count = 0
      for (const key of ['eggs', 'corpses']) {
        this[key] = this[key].filter((item) => {
          if (Math.hypot(item.x - x, item.y - y) > 42) return true
          count++
          return ++item.hits < 2
        })
      }
      return count
    }
  }
  root.FlyWorld = FlyWorld
  if (typeof module !== 'undefined') module.exports = FlyWorld
})(typeof globalThis === 'undefined' ? this : globalThis)
