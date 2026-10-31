import { zodResolver } from '@hookform/resolvers/zod'
import Button from '@mui/material/Button'
import Divider from '@mui/material/Divider'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { setupApi } from '../../api/endpoints'
import { useAuth } from '../../auth/authContext'
import { ErrorAlert } from '../../components/ErrorAlert'
import { AuthCard } from './AuthCard'

const schema = z.object({
  organizationName: z.string().trim().min(1, 'Enter the organization name').max(150),
  organizationCode: z.string().trim().regex(/^[A-Za-z0-9][A-Za-z0-9_-]{0,29}$/, 'Use up to 30 letters, digits, dashes or underscores'),
  adminFirstName: z.string().trim().min(1, 'Enter a first name'),
  adminLastName: z.string().trim().min(1, 'Enter a last name'),
  adminUserName: z.string().trim().min(3, 'Use at least 3 characters'),
  adminEmail: z.email('Enter a valid email address'),
  adminPassword: z
    .string()
    .min(10, 'Use at least 10 characters')
    .refine((v) => /[A-Za-z]/.test(v) && /\d/.test(v), 'Use at least one letter and one number'),
})

type FormValues = z.infer<typeof schema>

export function SetupPage() {
  const { t } = useTranslation()
  const { reload } = useAuth()
  const [error, setError] = useState<unknown>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const onSubmit = handleSubmit(async (values) => {
    setError(null)
    try {
      await setupApi.initialize({ ...values, organizationCode: values.organizationCode.toUpperCase() })
      await reload()
    } catch (err) {
      setError(err)
    }
  })

  const field = (name: keyof FormValues, label: string, extra: Record<string, unknown> = {}) => (
    <TextField
      id={`setup-${name}`}
      label={label}
      {...register(name)}
      error={!!errors[name]}
      helperText={errors[name]?.message ?? (extra.helperText as string | undefined)}
      {...extra}
    />
  )

  return (
    <AuthCard title={t('setup.title')} intro={t('setup.intro')} width={560}>
      <Stack component="form" spacing={2} onSubmit={onSubmit} noValidate>
        <ErrorAlert error={error} />
        <Typography variant="h3" component="h2">{t('setup.organization')}</Typography>
        {field('organizationName', t('setup.organizationName'))}
        {field('organizationCode', t('setup.organizationCode'), { helperText: t('setup.organizationCodeHelp') })}
        <Divider />
        <Typography variant="h3" component="h2">{t('setup.admin')}</Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          {field('adminFirstName', t('setup.firstName'))}
          {field('adminLastName', t('setup.lastName'))}
        </Stack>
        {field('adminUserName', t('setup.userName'), { autoComplete: 'username' })}
        {field('adminEmail', t('setup.email'), { type: 'email', autoComplete: 'email' })}
        {field('adminPassword', t('setup.password'), { type: 'password', autoComplete: 'new-password', helperText: t('setup.passwordHelp') })}
        <Button type="submit" variant="contained" size="large" disabled={isSubmitting}>
          {t('setup.submit')}
        </Button>
      </Stack>
    </AuthCard>
  )
}
