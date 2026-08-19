<script setup lang="ts">
import { computed, ref } from "vue";
import { useRouter } from "vue-router";
import { useMyFetch } from "@/store/server";
import { useConfigStore } from "@/store/config";
import { ActionScheme } from "@/types/config";
import { ahkToDisplay, createScheme } from "@/components/action/constants";

const store = useConfigStore()
const router = useRouter()

const schemes = computed(() => store.actionSchemes ?? [])

const deleteDialog = ref(false)
const deletingScheme = ref<ActionScheme | null>(null)

function openScheme(scheme: ActionScheme) {
  router.push(`/keymap/action/${scheme.id}`)
}

async function createNew() {
  // 通过 API 创建 (后端分配 id 并保存)
  const { data, error } = await useMyFetch("/api/action-schemes", { timeout: 3000 })
    .post(createScheme())
    .json()
  if (error.value) {
    alert("创建失败, 请确认设置程序正在运行: " + error.value.message)
    return
  }
  const scheme = data.value as ActionScheme
  // 同步到本地 store
  store.actionSchemes!.push(scheme)
  router.push(`/keymap/action/${scheme.id}`)
}

function askDelete(scheme: ActionScheme) {
  deletingScheme.value = scheme
  deleteDialog.value = true
}

function confirmDelete() {
  if (!deletingScheme.value) return
  const idx = store.actionSchemes!.findIndex(s => s.id == deletingScheme.value!.id)
  if (idx >= 0) {
    store.actionSchemes!.splice(idx, 1)
    store.saveConfig()
  }
  deleteDialog.value = false
  deletingScheme.value = null
}

function ruleCount(scheme: ActionScheme) {
  return scheme.rules?.length ?? 0
}

function toggleEnable(scheme: ActionScheme, val?: boolean) {
  scheme.enable = !!val
  store.saveConfig()
}
</script>

<template>
  <div class="pa-4">
    <div class="d-flex align-center mb-4">
      <h2 class="text-h6">选中动作</h2>
      <v-spacer></v-spacer>
      <v-btn color="primary" prepend-icon="mdi-plus" @click="createNew">新建方案</v-btn>
    </div>

    <v-alert type="info" density="compact" class="mb-4" variant="tonal">
      在任意窗口选中文本或文件后按下方案的快捷键, 即可触发预设行为。变量 <code>%selected%</code> 表示当前选中的内容。
    </v-alert>

    <v-row>
      <v-col v-for="scheme in schemes" :key="scheme.id" cols="12" sm="6" md="4">
        <v-card class="scheme-card" @click="openScheme(scheme)">
          <v-card-title class="d-flex align-center">
            <v-icon icon="mdi-gesture-tap" class="mr-2"
                    :color="scheme.enable ? 'primary' : 'grey'"></v-icon>
            <span class="text-subtitle-1">{{ scheme.name || "(未命名)" }}</span>
            <v-spacer></v-spacer>
            <v-switch
              :model-value="scheme.enable"
              density="compact"
              hide-details
              color="primary"
              @click.stop
              @update:model-value="toggleEnable(scheme, $event)"
            >
              <template #label>
                <span :class="scheme.enable ? 'text-primary' : 'text-grey'">
                  {{ scheme.enable ? '启用' : '关闭' }}
                </span>
              </template>
            </v-switch>
          </v-card-title>
          <v-card-text>
            <div class="d-flex align-center mb-1">
              <v-icon icon="mdi-keyboard-outline" size="18" class="mr-1"></v-icon>
              <span :class="scheme.hotkey ? 'hotkey-text' : 'text-grey'">
                {{ scheme.hotkey ? ahkToDisplay(scheme.hotkey) : "(未设置快捷键)" }}
              </span>
            </div>
            <div class="text-caption text-grey">{{ ruleCount(scheme) }} 条规则</div>
          </v-card-text>
          <v-card-actions>
            <v-spacer></v-spacer>
            <v-btn size="small" variant="text" @click.stop="openScheme(scheme)">编辑</v-btn>
            <v-btn size="small" variant="text" color="error" @click.stop="askDelete(scheme)">删除</v-btn>
          </v-card-actions>
        </v-card>
      </v-col>

      <v-col v-if="schemes.length == 0" cols="12">
        <v-empty-state
          icon="mdi-gesture-tap"
          title="还没有选中动作方案"
          text="点击右上角「新建方案」创建第一个方案"
        ></v-empty-state>
      </v-col>
    </v-row>

    <v-dialog v-model="deleteDialog" max-width="400">
      <v-card>
        <v-card-title>删除方案</v-card-title>
        <v-card-text>
          确定删除方案「{{ deletingScheme?.name }}」吗? 此操作会立即保存并重启 MyKeymap。
        </v-card-text>
        <v-card-actions>
          <v-spacer></v-spacer>
          <v-btn variant="text" @click="deleteDialog = false">取消</v-btn>
          <v-btn color="error" variant="text" @click="confirmDelete">删除</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </div>
</template>

<style scoped>
.scheme-card {
  cursor: pointer;
  transition: box-shadow 0.2s;
}

.scheme-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.hotkey-text {
  font-family: monospace;
  font-weight: bold;
  color: #4169e1;
}
</style>
