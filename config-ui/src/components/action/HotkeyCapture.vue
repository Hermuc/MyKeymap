<script setup lang="ts">
import { computed, ref } from "vue";
import { MODIFIER_CODE_MAP, ahkToDisplay, buildAhkFromCodes, keyToAhkName, normalizeHotkey } from "./constants";

const props = defineProps<{
  modelValue: string   // AHK 格式热键, 如 ^+q / <^q
  usedHotkeys?: Set<string>  // 已占用的热键 (归一化后)
}>()
const emit = defineEmits<{
  (e: "update:modelValue", value: string): void
}>()

const capturing = ref(false)
// 已按下的修饰键 (KeyboardEvent.code 数组, 按下顺序), 等待主键组成完整热键
const pendingMods = ref<string[]>([])

const display = computed(() => {
  if (!capturing.value) return ahkToDisplay(props.modelValue)
  if (pendingMods.value.length > 0) {
    return pendingMods.value.map(c => MODIFIER_CODE_MAP[c].label).join(" + ") + " + ..."
  }
  return "请按下快捷键..."
})

const conflict = computed(() => {
  if (!props.modelValue) return ""
  const normalized = normalizeHotkey(props.modelValue)
  if (props.usedHotkeys?.has(normalized)) {
    return "该快捷键与现有设置冲突"
  }
  return ""
})

function startCapture() {
  capturing.value = true
  pendingMods.value = []
}

function onKeydown(e: KeyboardEvent) {
  if (!capturing.value) return
  e.preventDefault()
  e.stopPropagation()
  // Esc 单独按下时取消捕获
  if (e.key === "Escape" && !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey) {
    capturing.value = false
    pendingMods.value = []
    return
  }
  // 修饰键: 暂存等待主键 (e.repeat 是按住不放的重复事件, 忽略)
  const mod = MODIFIER_CODE_MAP[e.code]
  if (mod && !e.repeat) {
    if (!pendingMods.value.includes(e.code)) {
      pendingMods.value.push(e.code)
    }
    return
  }
  // 主键: 组合修饰键生成 AHK 格式热键
  const key = keyToAhkName(e)
  if (key) {
    const ahk = buildAhkFromCodes(pendingMods.value, key)
    emit("update:modelValue", ahk)
    capturing.value = false
    pendingMods.value = []
  }
}

// 修饰键松开且未按主键时撤销暂存, 继续等待
function onKeyup(e: KeyboardEvent) {
  if (!capturing.value) return
  const idx = pendingMods.value.indexOf(e.code)
  if (idx >= 0) {
    pendingMods.value.splice(idx, 1)
  }
}

// 焦点离开输入框时取消捕获 (blur 不冒泡, 用 focusout)
function onFocusOut() {
  capturing.value = false
  pendingMods.value = []
}
</script>

<template>
  <div class="hotkey-capture" @keydown="onKeydown" @keyup="onKeyup" @focusout="onFocusOut">
    <v-text-field
      :model-value="display"
      :placeholder="capturing ? '请按下快捷键...' : '点击后按下快捷键'"
      readonly
      hide-details="auto"
      density="compact"
      variant="outlined"
      :error="!!conflict"
      :error-messages="conflict"
      @click="startCapture"
    >
      <template #prepend-inner>
        <v-icon :icon="capturing ? 'mdi-keyboard-outline' : 'mdi-keyboard-settings-outline'" size="22"></v-icon>
      </template>
      <template #append-inner>
        <v-btn
          v-if="modelValue && !capturing"
          icon="mdi-close"
          size="x-small"
          variant="text"
          @click="emit('update:modelValue', '')"
        ></v-btn>
      </template>
    </v-text-field>
  </div>
</template>

<style scoped>
.hotkey-capture :deep(.v-input) {
  cursor: pointer;
}
</style>
