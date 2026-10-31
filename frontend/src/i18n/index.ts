import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './en.json'

// Hindi and Marathi resource files are added in Phase 4; every screen already reads text through t().
void i18n.use(initReactI18next).init({
  resources: { en: { translation: en } },
  lng: 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
})

export default i18n
