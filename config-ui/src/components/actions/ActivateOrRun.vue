<script lang="ts" setup>
import { useConfigStore } from '@/store/config';
import { server } from '@/store/server';
import { useShortcutStore } from '@/store/shortcut';
import { storeToRefs } from 'pinia';
import { watchEffect } from 'vue';

const { action } = storeToRefs(useConfigStore())
const { shortcuts } = storeToRefs(useShortcutStore())
const { translate } = useConfigStore()


watchEffect(() => {
  action.value.isEmpty = !action.value.winTitle && !action.value.target
})

// winTitle 校验: 裸写 "xxx.exe" 会被 AHK 当作窗口标题子串匹配, 永远匹配失败
const winTitleRules = [
  (v: string) => {
    if (!v || v.startsWith('ahk_') || v.startsWith('ahk-expression:')) return true
    if (/\.exe$/i.test(v)) return translate('label:301err')
    return true
  },
]

</script>

<template>
  <v-text-field color="primary" autocomplete="off" variant="underlined" :label="translate('label:301')" v-model="action.winTitle" :rules="winTitleRules" persistent-hint :hint="translate('label:301hint')" />
  <v-combobox class="input"
              color="primary"
              :label="translate('label:302')"
              :items="shortcuts"
              :hide-no-data="true"
              :menu-props="{ maxHeight: 150 }"
              v-model="action.target"
              variant="underlined"></v-combobox>
  <v-text-field color="primary" autocomplete="off" variant="underlined" :label="translate('label:303')" v-model="action.args" />
  <v-text-field color="primary" autocomplete="off" variant="underlined" :label="translate('label:304')" v-model="action.workingDir" />
  <v-text-field color="primary" autocomplete="off" variant="underlined" :label="translate('label:305')" v-model="action.comment" />
  <v-card-actions class="card-actions">
    <v-checkbox hide-details :label="translate('label:306')" color="secondary" v-model="action.runAsAdmin" />
    <v-checkbox hide-details :label="translate('label:307')" color="secondary" v-model="action.runInBackground" />
    <v-checkbox hide-details :label="translate('label:308')" color="secondary" v-model="action.detectHiddenWindow" />
    <v-btn class="action-button text-none" color="primary" variant="outlined" @click="server.runWindowSpy">{{ translate('label:309') }}</v-btn>
  </v-card-actions>
</template>

<style scoped>
.card-actions {
  margin-top: -18px;
  margin-left: -18px;
}

.action-button {
  margin-top: -18px;
  margin-right: 17px;
}

.input :deep(i) {
  visibility: hidden;
}
</style>
