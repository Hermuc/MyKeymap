<script setup lang="ts">
import { ref } from "vue";
import { ActionRule } from "@/types/config";
import { ACTION_TYPES, FILE_GROUPS, MATCH_TYPES } from "./constants";

const props = defineProps<{
  rules: Array<ActionRule>
  selectedIndex: number
  matchedPriority?: number  // 测试命中的规则 priority, 用于高亮
}>()
const emit = defineEmits<{
  (e: "select", index: number): void
  (e: "delete", index: number): void
  (e: "move", index: number, dir: -1 | 1): void
  (e: "drop", from: number, to: number): void
}>()

const dragIndex = ref<number>(-1)
const dragOverIndex = ref<number>(-1)

function matchTypeLabel(type: string) {
  return MATCH_TYPES.find(x => x.value == type)?.label ?? type
}

function actionTypeLabel(type: string) {
  return ACTION_TYPES.find(x => x.value == type)?.label ?? type
}

function matchValueText(rule: ActionRule) {
  if (rule.matchType == "default") return "任意内容"
  if (rule.matchType == "fileGroup") {
    return FILE_GROUPS.find(g => g.value == rule.matchValue)?.label ?? rule.matchValue
  }
  return rule.matchValue || "(未设置)"
}

function actionValueText(rule: ActionRule) {
  if (rule.actionType == "search") return "搜索: " + rule.actionValue
  if (rule.actionType == "send_keys") return "按键: " + rule.actionValue
  if (rule.actionType == "copy") return "复制: " + rule.actionValue
  return rule.actionValue
}

function onDragStart(index: number) {
  dragIndex.value = index
}

function onDragOver(index: number) {
  dragOverIndex.value = index
}

function onDrop(index: number) {
  if (dragIndex.value >= 0 && dragIndex.value != index) {
    emit("drop", dragIndex.value, index)
  }
  dragIndex.value = -1
  dragOverIndex.value = -1
}

function onDragEnd() {
  dragIndex.value = -1
  dragOverIndex.value = -1
}
</script>

<template>
  <v-list class="rule-list" density="compact" nav>
    <v-list-item
      v-for="(rule, index) in rules"
      :key="index"
      :active="index == selectedIndex"
      :class="{ 'matched-rule': rule.priority == matchedPriority, 'drag-over': dragOverIndex == index }"
      draggable="true"
      @click="emit('select', index)"
      @dragstart="onDragStart(index)"
      @dragover.prevent="onDragOver(index)"
      @drop.prevent="onDrop(index)"
      @dragend="onDragEnd"
    >
      <template #prepend>
        <v-icon icon="mdi-drag-vertical" size="20" class="drag-handle"></v-icon>
        <v-chip size="x-small" class="ml-1" color="grey">{{ index + 1 }}</v-chip>
      </template>
      <v-list-item-title class="text-body-2">
        <span class="text-primary">{{ matchTypeLabel(rule.matchType) }}</span>
        <span class="text-grey"> → </span>
        <span>{{ matchValueText(rule) }}</span>
      </v-list-item-title>
      <v-list-item-subtitle class="text-caption">
        {{ actionTypeLabel(rule.actionType) }} · {{ actionValueText(rule) || "(未设置行为)" }}
      </v-list-item-subtitle>
      <template #append>
        <v-btn
          icon="mdi-arrow-up"
          size="x-small"
          variant="text"
          :disabled="index == 0"
          @click.stop="emit('move', index, -1)"
        ></v-btn>
        <v-btn
          icon="mdi-arrow-down"
          size="x-small"
          variant="text"
          :disabled="index == rules.length - 1"
          @click.stop="emit('move', index, 1)"
        ></v-btn>
        <v-btn
          icon="mdi-delete-outline"
          size="x-small"
          variant="text"
          color="error"
          @click.stop="emit('delete', index)"
        ></v-btn>
      </template>
    </v-list-item>
  </v-list>
</template>

<style scoped>
.rule-list {
  max-height: 460px;
  overflow-y: auto;
}

.drag-handle {
  cursor: grab;
}

.drag-over {
  outline: 2px dashed #4169e1;
  outline-offset: -2px;
}

.matched-rule {
  background: rgba(65, 105, 225, 0.12) !important;
}
</style>
