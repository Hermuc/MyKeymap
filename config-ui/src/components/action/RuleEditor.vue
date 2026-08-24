<script setup lang="ts">
import { computed } from "vue";
import { ActionRule } from "@/types/config";
import { useConfigStore } from "@/store/config";
import { ACTION_TYPES, DEFAULT_SEARCH_URL, MATCH_TYPES, TEXT_TYPES, TEXT_TYPE_ACTIONS, TEXT_TYPE_DEFAULT_ACTION, TEXT_TYPE_LABELS, TEXT_ACTIONS } from "./constants";

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
    const value = v == "textType" ? "url" : v == "default" ? "*" : ""
    emit("update", { ...props.rule, matchType: v, matchValue: value })
  },
})

const actionType = computed({
  get: () => props.rule.actionType,
  set: (v: string) => {
    // 切换行为类型时给出默认模板; 文本特征专用行为不接受命令模板, actionValue 置空
    let value = props.rule.actionValue
    if (TEXT_ACTIONS.has(v)) {
      value = ""
    } else if (v == "search" && !value) {
      value = DEFAULT_SEARCH_URL
    } else if (v == "run" && !value) {
      value = "%selected%"
    }
    emit("update", { ...props.rule, actionType: v, actionValue: value })
  },
})

const matchTypeMeta = computed(() => MATCH_TYPES.find(x => x.value == props.rule.matchType))
const actionTypeMeta = computed(() => ACTION_TYPES.find(x => x.value == props.rule.actionType))

// 行为下拉选项: 文本特征 (textType) 时随特征动态联动, 其余匹配类型展示全量行为
const actionTypeItems = computed(() => {
  if (props.rule.matchType != "textType") return ACTION_TYPES
  const allowed = TEXT_TYPE_ACTIONS[props.rule.matchValue] ?? []
  return ACTION_TYPES.filter(x => allowed.includes(x.value))
})

// 是否文本特征专用行为 (无命令模板)
const isTextAction = computed(() => TEXT_ACTIONS.has(props.rule.actionType))

function update(partial: Partial<ActionRule>) {
  emit("update", { ...props.rule, ...partial })
}

// 切换文本特征: 若当前行为不在新特征的可选范围内, 自动纠正为默认行为 (联动规则, 见 constants.ts TEXT_TYPE_ACTIONS)
function onTextTypeChange(v: string) {
  const allowed = TEXT_TYPE_ACTIONS[v] ?? []
  let actionType = props.rule.actionType
  if (!allowed.includes(actionType)) {
    actionType = TEXT_TYPE_DEFAULT_ACTION[v] ?? ""
  }
  // 纠正到无参行为时清空命令模板, 避免残留模板误导
  const actionValue = TEXT_ACTIONS.has(actionType) ? "" : props.rule.actionValue
  update({ matchValue: v, actionType, actionValue })
}

// 各匹配类型的条件值控件
const showMatchValueInput = computed(() => !["textType", "default"].includes(props.rule.matchType))

// 文件分组 (来自配置 fileGroups, 仅作「文件后缀」条件值的快捷填充; 选择后展开为逗号分隔后缀列表, 可继续手改)
const configStore = useConfigStore()
const fileGroups = computed(() => configStore.config?.fileGroups ?? [])

function onFileGroupFill(name: string | null) {
  if (!name) return
  const group = fileGroups.value.find(g => g.name == name)
  if (group) update({ matchValue: group.exts.join(", ") })
}
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
        <v-col cols="12" v-if="rule.matchType == 'fileExt' && fileGroups.length > 0">
          <v-select
            :model-value="null"
            :items="fileGroups"
            item-title="label"
            item-value="name"
            label="常用分组快捷填入"
            density="compact"
            variant="outlined"
            clearable
            :hint="'选择分组自动填入后缀列表, 可继续手改'"
            persistent-hint
            @update:model-value="onFileGroupFill($event)"
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
            :hint="'行为将随特征联动: ' + (TEXT_TYPE_LABELS[rule.matchValue] ?? rule.matchValue)"
            persistent-hint
            @update:model-value="onTextTypeChange($event)"
          ></v-select>
        </v-col>
      </v-row>

      <v-divider class="my-3"></v-divider>

      <v-row dense>
        <v-col cols="12">
          <v-select
            :model-value="rule.actionType"
            :items="actionTypeItems"
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
            v-if="!isTextAction"
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
          <v-alert v-else type="info" density="compact" variant="tonal" class="mb-1">
            该行为直接作用于选中内容 ({{ actionTypeMeta?.hint }}), 无需配置命令模板。
          </v-alert>
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
