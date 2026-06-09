import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: '/gakumasu-calc/',
  test: {
    environment: 'node',
    include: ['tests/**/*.test.ts'],
    // マルチスタート(3スタート)で実データテストが重くなるため余裕を持たせる
    testTimeout: 60000,
    // 実行ごとに全テスト名を表示 (verbose) し、JUnit XML をリポジトリ直下 test-results/ に出力
    reporters: ['verbose', 'junit'],
    outputFile: { junit: '../test-results/vitest-junit.xml' },
  },
})
