<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { useConfigStore } from "@/store/config";
import { ActionRule } from "@/types/config";
import HotkeyCapture from "@/components/action/HotkeyCapture.vue";
import RuleList from "@/components/action/RuleList.vue";
import RuleEditor from "@/components/action/RuleEditor.vue";
import ActionTester from "@/components/action/ActionTester.vue";
import { collectUsedHotkeys, createRule, ACTION_TYPES, TEXT_TYPE_ACTIONS, TEXT_TYPE_LABELS } from "@/components/action/constants";

const store = useConfigStore()
const route = useRoute()
const router = useRouter()

const scheme = computed(() => store.actionSchemes?.find(s => s.id == Number(route.params.id)))
const selectedRuleIndex = ref(0)
const matchedPriority = ref<number | undefined>(undefined)

// 已占用的热键 (现有 keymaps + 其他方案的热键)
const usedHotkeys = computed(() => {
  const used = collectUsedHotkeys(store.keymaps)
  for (const s of store.actionSchemes ?? []) {
    if (s.id != scheme.value?.id && s.hotkey) {
      used.add(s.hotkey.replace(/^[*~$]+/, "").toLowerCase())
    }
  }
  return used
})

const selectedRule = computed(() => scheme.value?.rules[selectedRuleIndex.value])

// default (兜底) 规则之后还有其他规则时, 这些规则永远不会被匹配到 (第一个命中的规则生效)
const defaultRuleNotLast = computed(() => {
  const rules = scheme.value?.rules ?? []
  const idx = rules.findIndex(r => r.matchType == "default")
  return idx >= 0 && idx < rules.length - 1
})

// 切换方案时重置编辑状态
watch(() => route.params.id, () => {
  selectedRuleIndex.value = 0
  matchedPriority.value = undefined
})

function updateRule(rule: ActionRule) {
  if (scheme.value && selectedRuleIndex.value < scheme.value.rules.length) {
    scheme.value.rules[selectedRuleIndex.value] = rule
  }
}

function addRule() {
  if (!scheme.value) return
  const maxPriority = scheme.value.rules.reduce((max, r) => Math.max(max, r.priority), 0)
  scheme.value.rules.push(createRule(maxPriority + 1))
  selectedRuleIndex.value = scheme.value.rules.length - 1
}

function deleteRule(index: number) {
  if (!scheme.value) return
  scheme.value.rules.splice(index, 1)
  if (selectedRuleIndex.value >= scheme.value.rules.length) {
    selectedRuleIndex.value = scheme.value.rules.length - 1
  }
}

function moveRule(index: number, dir: -1 | 1) {
  if (!scheme.value) return
  const target = index + dir
  if (target < 0 || target >= scheme.value.rules.length) return
  const [rule] = scheme.value.rules.splice(index, 1)
  scheme.value.rules.splice(target, 0, rule)
  selectedRuleIndex.value = target
}

function dropRule(from: number, to: number) {
  if (!scheme.value) return
  const [rule] = scheme.value.rules.splice(from, 1)
  scheme.value.rules.splice(to, 0, rule)
  selectedRuleIndex.value = to
}

// ===== 导入导出 =====
function exportScheme() {
  if (!scheme.value) return
  const json = JSON.stringify(scheme.value, null, 2)
  const blob = new Blob([json], { type: "application/json" })
  const url = URL.createObjectURL(blob)
  const a = document.createElement("a")
  a.href = url
  a.download = `action-scheme-${scheme.value.id}-${scheme.value.name || "unnamed"}.json`
  a.click()
  URL.revokeObjectURL(url)
}

const importDialog = ref(false)
const importText = ref("")
const importError = ref("")

function openImport() {
  importText.value = ""
  importError.value = ""
  importDialog.value = true
}

function confirmImport() {
  if (!scheme.value) return
  try {
    const parsed = JSON.parse(importText.value)
    if (!parsed.rules || !Array.isArray(parsed.rules)) {
      importError.value = "JSON 格式不正确: 缺少 rules 数组"
      return
    }
    // 校验 textType 特征与行为的组合合法性 (与后端保存校验一致, 非法组合拒绝导入)
    for (const [i, r] of parsed.rules.entries()) {
      if (r.matchType == "textType") {
        const allowed = TEXT_TYPE_ACTIONS[r.matchValue] ?? []
        if (!allowed.includes(r.actionType)) {
          const actionLabel = ACTION_TYPES.find(x => x.value == r.actionType)?.label ?? r.actionType
          importError.value = `第 ${i + 1} 条规则: 文本特征「${TEXT_TYPE_LABELS[r.matchValue] ?? r.matchValue}」与行为「${actionLabel}」不匹配, 无法导入`
          return
        }
      }
    }
    // 只导入规则集, 保留当前方案的 id/name/hotkey/enable
    // 按数组顺序重写 priority, 保证与匹配顺序一致 (手工编辑的导入 JSON 可能乱序, 会导致测试命中高亮错位)
    scheme.value.rules = parsed.rules.map((r: ActionRule, i: number) => ({ ...r, priority: i + 1 }))
    selectedRuleIndex.value = 0
    importDialog.value = false
  } catch (e) {
    importError.value = "JSON 解析失败: " + (e as Error).message
  }
}

// ===== 删除方案 =====
const deleteDialog = ref(false)

function confirmDelete() {
  if (!scheme.value) return
  const idx = store.actionSchemes!.findIndex(s => s.id == scheme.value!.id)
  if (idx >= 0) {
    store.actionSchemes!.splice(idx, 1)
    store.saveConfig()
  }
  router.push("/keymap/action")
}

function goBack() {
  router.push("/keymap/action")
}
</script>

<template>
  <div v-if="scheme" class="pa-4">
    <!-- 顶部: 方案信息 -->
    <div class="d-flex align-center mb-2">
      <v-btn icon="mdi-arrow-left" variant="text" @click="goBack"></v-btn>
      <v-text-field
        v-model="scheme.name"
        label="方案名称"
        density="compact"
        variant="outlined"
        class="mx-2"
        style="max-width: 280px"
        hide-details
      ></v-text-field>
      <div style="width: 260px">
        <HotkeyCapture v-model="scheme.hotkey" :used-hotkeys="usedHotkeys"></HotkeyCapture>
      </div>
      <v-switch v-model="scheme.enable" color="primary" density="compact" hide-details class="mx-2">
              <template #label>
                <span :class="scheme.enable ? 'text-primary' : 'text-grey'">
                  {{ scheme.enable ? '启用' : '关闭' }}
                </span>
              </template>
            </v-switch>
      <v-spacer></v-spacer>
      <v-btn color="primary" prepend-icon="mdi-content-save-outline" variant="outlined" class="mr-2"
             @click="store.saveConfig()">保存</v-btn>
      <v-btn prepend-icon="mdi-export" variant="outlined" class="mr-2" @click="exportScheme">导出</v-btn>
      <v-btn prepend-icon="mdi-import" variant="outlined" class="mr-2" @click="openImport">导入</v-btn>
      <v-btn color="error" prepend-icon="mdi-delete-outline" variant="text" @click="deleteDialog = true">删除方案</v-btn>
    </div>

    <v-alert v-if="!scheme.hotkey" type="warning" density="compact" variant="tonal" class="mb-2">
      尚未设置快捷键, 保存后方案不会生效。
    </v-alert>

    <!-- 中部: 规则列表 + 规则编辑 -->
    <v-row class="mt-2">
      <v-col cols="12" md="5" lg="4">
        <v-card flat class="pa-2 rule-panel">
          <v-card-title class="text-subtitle-1 d-flex align-center">
            <v-icon icon="mdi-format-list-bulleted" class="mr-2"></v-icon>
            规则列表
            <v-spacer></v-spacer>
            <v-tooltip text="匹配优先级: 从上到下, 第一个命中的规则生效">
              <template #activator="{ props }">
                <v-icon v-bind="props" icon="mdi-information-outline" size="18" class="text-grey"></v-icon>
              </template>
            </v-tooltip>
          </v-card-title>
          <v-alert v-if="defaultRuleNotLast" type="warning" density="compact" variant="tonal" class="mb-2">
            「默认 (兜底)」规则之后的规则永远不会被匹配, 请把默认规则移到最后
          </v-alert>
          <RuleList
            :rules="scheme.rules"
            :selected-index="selectedRuleIndex"
            :matched-priority="matchedPriority"
            @select="selectedRuleIndex = $event"
            @delete="deleteRule"
            @move="moveRule"
            @drop="dropRule"
          ></RuleList>
          <v-btn color="primary" variant="outlined" prepend-icon="mdi-plus" class="mt-2 w-100"
                 @click="addRule">添加规则</v-btn>
        </v-card>
      </v-col>

      <v-col cols="12" md="7" lg="8">
        <RuleEditor v-if="selectedRule" :rule="selectedRule" @update="updateRule"></RuleEditor>
        <v-empty-state v-else icon="mdi-format-list-bulleted" title="暂无规则"
                       text="点击左侧「添加规则」开始配置"></v-empty-state>
      </v-col>
    </v-row>

    <!-- 底部: 模拟测试 -->
    <div class="mt-4">
      <ActionTester :scheme-id="scheme.id" :scheme="scheme" @matched="matchedPriority = $event"></ActionTester>
    </div>

    <!-- 导入对话框 -->
    <v-dialog v-model="importDialog" max-width="560">
      <v-card>
        <v-card-title>导入规则</v-card-title>
        <v-card-text>
          <p class="text-caption text-grey mb-2">粘贴导出的方案 JSON, 将替换当前方案的规则集 (方案名称与快捷键保持不变)。</p>
          <v-textarea v-model="importText" label="方案 JSON" rows="8" variant="outlined"></v-textarea>
          <v-alert v-if="importError" type="error" density="compact">{{ importError }}</v-alert>
        </v-card-text>
        <v-card-actions>
          <v-spacer></v-spacer>
          <v-btn variant="text" @click="importDialog = false">取消</v-btn>
          <v-btn color="primary" variant="text" @click="confirmImport">导入</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 删除方案对话框 -->
    <v-dialog v-model="deleteDialog" max-width="400">
      <v-card>
        <v-card-title>删除方案</v-card-title>
        <v-card-text>
          确定删除方案「{{ scheme.name }}」吗? 此操作会立即保存并重启 MyKeymap。
        </v-card-text>
        <v-card-actions>
          <v-spacer></v-spacer>
          <v-btn variant="text" @click="deleteDialog = false">取消</v-btn>
          <v-btn color="error" variant="text" @click="confirmDelete">删除</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </div>

  <div v-else class="pa-4">
    <v-alert type="error">方案不存在</v-alert>
    <v-btn class="mt-3" prepend-icon="mdi-arrow-left" @click="goBack">返回列表</v-btn>
  </div>
</template>

<style scoped>
.rule-panel {
  border: 1px solid rgba(0, 0, 0, 0.12);
  border-radius: 8px;
  height: 100%;
}
</style>
