<script setup lang="ts">
import { computed } from "vue";
import { ActionRule } from "@/types/config";
import { ACTION_TYPES, DEFAULT_SEARCH_URL, FILE_GROUPS, MATCH_TYPES, TEXT_TYPES } from "./constants";

const props = defineProps<{
  rule: ActionRule
}>()
const emit = defineEmits<{
  (e: "update", rule: ActionRule): void
}>()

const matchType = computed({
  get: () => props.rule.matchType,
  set: (v: string) => {
    // 切换匹配类型时重置条件值
    const value = v == "fileGroup" ? "image" : v == "textType" ? "url" : v == "default" ? "*" : ""
    emit("update", { ...props.rule, matchType: v, matchValue: value })
  },
})

const actionType = computed({
  get: () => props.rule.actionType,
  set: (v: string) => {
    // 切换行为类型时给出默认模板
    let value = props.rule.actionValue
    if (v == "search" && !value) {
      value = DEFAULT_SEARCH_URL
    }
    if (v == "run" && !value) {
      value = "%selected%"
    }
    emit("update", { ...props.rule, actionType: v, actionValue: value })
  },
})

const matchTypeMeta = computed(() => MATCH_TYPES.find(x => x.value == props.rule.matchType))
const actionTypeMeta = computed(() => ACTION_TYPES.find(x => x.value == props.rule.actionType))

function update(partial: Partial<ActionRule>) {
  emit("update", { ...props.rule, ...partial })
}

// 各匹配类型的条件值控件
const showMatchValueInput = computed(() => !["fileGroup", "textType", "default"].includes(props.rule.matchType))
</script>

<template>
  <v-card class="rule-editor" flat>
    <v-card-text>
      <v-row dense>
        <v-col cols="12">
          <v-select
            :model-value="rule.matchType"
            :items="MATCH_TYPES"
            item-title="label"
            item-value="value"
            label="匹配类型"
            density="compact"
            variant="outlined"
            @update:model-value="matchType = $event"
          ></v-select>
        </v-col>
      </v-row>

      <v-row dense>
        <v-col cols="12" v-if="showMatchValueInput">
          <v-text-field
            :model-value="rule.matchValue"
            label="条件值"
            density="compact"
            variant="outlined"
            :placeholder="matchTypeMeta?.hint"
            :hint="matchTypeMeta?.hint"
            persistent-hint
            @update:model-value="update({ matchValue: $event })"
          ></v-text-field>
        </v-col>
        <v-col cols="12" v-else-if="rule.matchType == 'fileGroup'">
          <v-select
            :model-value="rule.matchValue"
            :items="FILE_GROUPS"
            item-title="label"
            item-value="value"
            label="文件分组"
            density="compact"
            variant="outlined"
            :hint="'后缀: ' + (FILE_GROUPS.find(g => g.value == rule.matchValue)?.exts ?? '')"
            persistent-hint
            @update:model-value="update({ matchValue: $event })"
          ></v-select>
        </v-col>
        <v-col cols="12" v-else-if="rule.matchType == 'textType'">
          <v-select
            :model-value="rule.matchValue"
            :items="TEXT_TYPES"
            item-title="label"
            item-value="value"
            label="文本特征"
            density="compact"
            variant="outlined"
            @update:model-value="update({ matchValue: $event })"
          ></v-select>
        </v-col>
      </v-row>

      <v-divider class="my-3"></v-divider>

      <v-row dense>
        <v-col cols="12">
          <v-select
            :model-value="rule.actionType"
            :items="ACTION_TYPES"
            item-title="label"
            item-value="value"
            label="行为类型"
            density="compact"
            variant="outlined"
            @update:model-value="actionType = $event"
          ></v-select>
        </v-col>
      </v-row>

      <v-row dense>
        <v-col cols="12">
          <v-textarea
            :model-value="rule.actionValue"
            label="目标命令 / URL / 脚本"
            density="compact"
            variant="outlined"
            rows="2"
            auto-grow
            :placeholder="actionTypeMeta?.hint"
            :hint="actionTypeMeta?.hint"
            persistent-hint
            @update:model-value="update({ actionValue: $event })"
          ></v-textarea>
        </v-col>
      </v-row>

      <v-row dense v-if="rule.actionType == 'run'">
        <v-col cols="12">
          <v-text-field
            :model-value="rule.workingDir"
            label="工作目录 (可选)"
            density="compact"
            variant="outlined"
            @update:model-value="update({ workingDir: $event })"
          ></v-text-field>
        </v-col>
      </v-row>

      <v-divider class="my-3"></v-divider>

      <v-row dense>
        <v-col cols="12">
          <v-checkbox
            :model-value="rule.options.copyToClipboard"
            label="执行后复制选中内容到剪贴板"
            density="compact"
            hide-details
            @update:model-value="update({ options: { ...rule.options, copyToClipboard: $event } })"
          ></v-checkbox>
        </v-col>
        <v-col cols="12">
          <v-checkbox
            :model-value="rule.options.clearSelection"
            label="执行后清空选中"
            density="compact"
            hide-details
            @update:model-value="update({ options: { ...rule.options, clearSelection: $event } })"
          ></v-checkbox>
        </v-col>
        <v-col cols="12">
          <v-checkbox
            :model-value="rule.options.confirm"
            label="执行前显示确认提示"
            density="compact"
            hide-details
            @update:model-value="update({ options: { ...rule.options, confirm: $event } })"
          ></v-checkbox>
        </v-col>
      </v-row>
    </v-card-text>
  </v-card>
</template>

<style scoped>
.rule-editor {
  border: 1px solid rgba(0, 0, 0, 0.12);
  border-radius: 8px;
}
</style>
