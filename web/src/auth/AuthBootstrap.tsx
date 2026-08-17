import { Auth0Provider, useAuth0 } from '@auth0/auth0-react'
import { useEffect, useState, type ReactNode } from 'react'
import { registerAuth } from '../lib/api'
import { fetchClientConfig, type ClientConfig } from '../lib/config'
import { AuthGate, Splash } from './AuthGate'

type BootstrapState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; config: ClientConfig }

/**
 * Fetches the Auth0 settings from the API before anything renders, then hands them to
 * Auth0Provider. The settings cannot be baked into the bundle, so this round trip is
 * what keeps one Docker image usable against any tenant.
 */
export function AuthBootstrap({ children }: { children: ReactNode }) {
  const [state, setState] = useState<BootstrapState>({ status: 'loading' })

  useEffect(() => {
    let cancelled = false

    fetchClientConfig()
      .then((config) => !cancelled && setState({ status: 'ready', config }))
      .catch((error: Error) => !cancelled && setState({ status: 'error', message: error.message }))

    return () => {
      cancelled = true
    }
  }, [])

  if (state.status === 'loading') {
    return <Splash label="Contacting the server" />
  }

  if (state.status === 'error') {
    return <ServerUnreachable message={state.message} />
  }

  return (
    <Auth0Provider
      domain={state.config.auth0Domain}
      clientId={state.config.auth0ClientId}
      authorizationParams={{
        redirect_uri: window.location.origin,
        // Without an audience Auth0 issues an opaque token the API cannot validate.
        audience: state.config.auth0Audience,
        scope: 'openid profile email',
      }}
      // Survives a reload, so a refresh does not bounce the user back to sign-in.
      cacheLocation="localstorage"
      useRefreshTokens
    >
      <TokenBridge />
      <AuthGate>{children}</AuthGate>
    </Auth0Provider>
  )
}

/**
 * Hands the SDK's token getter to the plain API client, which has no React dependency
 * of its own.
 */
function TokenBridge() {
  const { getAccessTokenSilently, isAuthenticated, loginWithRedirect } = useAuth0()

  useEffect(() => {
    if (!isAuthenticated) {
      registerAuth(null)
      return
    }

    registerAuth(
      () => getAccessTokenSilently(),
      () => void loginWithRedirect(),
    )

    return () => registerAuth(null)
  }, [isAuthenticated, getAccessTokenSilently, loginWithRedirect])

  return null
}

function ServerUnreachable({ message }: { message: string }) {
  return (
    <div className="relative z-10 grid min-h-dvh place-items-center px-4">
      <div className="panel w-full max-w-md rounded-2xl px-7 py-8" role="alert">
        <h1 className="font-display text-2xl">Cannot reach the server</h1>
        <p className="mt-2 text-[13px] leading-snug text-ink-muted">
          Questward could not load its configuration, so it cannot start sign-in.
        </p>

        <p className="mt-3 rounded-lg border border-line bg-surface-sunk px-3 py-2 font-mono text-[11.5px] text-ink-muted">
          {message}
        </p>

        <button
          type="button"
          onClick={() => window.location.reload()}
          className="mt-5 rounded-lg border border-line px-4 py-2 text-xs font-medium text-ink-muted transition hover:border-gold hover:text-gold"
        >
          Try again
        </button>
      </div>
    </div>
  )
}
