<script setup lang="ts">
import { ref } from "vue";
import { useMyFetch } from "@/store/server";
import { ACTION_TYPES, MATCH_TYPES } from "./constants";
import { ActionScheme } from "@/types/config";

const props = defineProps<{
  schemeId: number
  // 编辑中的方案快照 (含未保存的修改), 传空时后端回退读取磁盘配置
  scheme?: ActionScheme
}>()
const emit = defineEmits<{
  (e: "matched", priority: number | undefined): void
}>()

const content = ref("https://example.com")
const isFile = ref(false)
const testing = ref(false)
const result = ref<null | { matched: boolean, rule?: any, preview?: string }>(null)
const error = ref("")

async function runTest() {
  if (!content.value.trim()) {
    error.value = "请输入模拟选中内容"
    return
  }
  testing.value = true
  error.value = ""
  result.value = null
  emit("matched", undefined)

  const { data, error: fetchError } = await useMyFetch("/api/action-schemes/test", { timeout: 3000 })
    .post({
      schemeId: props.schemeId,
      scheme: props.scheme,
      content: content.value,
      isFile: isFile.value,
    })
    .json()

  testing.value = false
  if (fetchError.value) {
    // 后端 400 时带具体原因 (如正则无法在测试环境解析), 优先展示响应体中的 message
    error.value = "测试失败: " + ((fetchError.value.data as any)?.message ?? fetchError.value.message)
    return
  }
  result.value = data.value as any
  if (data.value?.matched) {
    emit("matched", data.value.rule.priority)
  }
}

function matchTypeLabel(type: string) {
  return MATCH_TYPES.find(x => x.value == type)?.label ?? type
}

function actionTypeLabel(type: string) {
  return ACTION_TYPES.find(x => x.value == type)?.label ?? type
}
</script>

<template>
  <v-card class="action-tester" flat>
    <v-card-title class="text-subtitle-1 d-flex align-center">
      <v-icon icon="mdi-flask-outline" class="mr-2"></v-icon>
      模拟测试
    </v-card-title>
    <v-card-text>
      <v-textarea
        v-model="content"
        label="模拟选中内容 (文本或文件路径, 多文件用换行分隔)"
        density="compact"
        variant="outlined"
        rows="2"
        auto-grow
      ></v-textarea>
      <div class="d-flex align-center">
        <v-switch
          v-model="isFile"
          label="模拟文件选中"
          density="compact"
          hide-details
          class="mr-4"
        ></v-switch>
        <v-btn
          color="primary"
          variant="outlined"
          :loading="testing"
          prepend-icon="mdi-play"
          @click="runTest"
        >测试</v-btn>
      </div>

      <v-alert v-if="error" type="error" density="compact" class="mt-3">{{ error }}</v-alert>

      <div v-if="result" class="mt-3">
        <v-alert v-if="!result.matched" type="info" density="compact">
          没有匹配到任何规则
        </v-alert>
        <v-card v-else variant="tonal" color="primary">
          <v-card-text>
            <div><strong>命中的规则</strong> (优先级 {{ result.rule.priority }})</div>
            <div class="text-body-2 mt-1">
              {{ matchTypeLabel(result.rule.matchType) }}: {{ result.rule.matchValue }}
              <span class="text-grey"> → </span>
              {{ actionTypeLabel(result.rule.actionType) }}
            </div>
            <v-divider class="my-2"></v-divider>
            <div class="text-caption text-grey">执行预览</div>
            <code class="preview">{{ result.preview || "(无预览)" }}</code>
          </v-card-text>
        </v-card>
      </div>
    </v-card-text>
  </v-card>
</template>

<style scoped>
.action-tester {
  border: 1px solid rgba(0, 0, 0, 0.12);
  border-radius: 8px;
}

.preview {
  display: block;
  word-break: break-all;
  white-space: pre-wrap;
  background: rgba(0, 0, 0, 0.05);
  border-radius: 4px;
  padding: 6px 8px;
  font-size: 0.8rem;
}
</style>
