/**
 * Auth0 settings fetched from the API at runtime.
 *
 * Deliberately not `import.meta.env.VITE_*`: Vite inlines those at build time and the SPA
 * is built inside the Docker image, so baking them in would tie one image to one tenant.
 * This fetch is unauthenticated by necessity, since it happens before sign-in.
 */
export interface ClientConfig {
  auth0Domain: string
  auth0ClientId: string
  auth0Audience: string
}

export async function fetchClientConfig(): Promise<ClientConfig> {
  const response = await fetch('/api/config')

  if (!response.ok) {
    throw new Error(`The server returned ${response.status} for /api/config.`)
  }

  const config = (await response.json()) as ClientConfig

  if (!config.auth0Domain || !config.auth0ClientId || !config.auth0Audience) {
    throw new Error(
      'The server is running without complete Auth0 settings. Check Auth0__Domain, ' +
        'Auth0__Audience and Auth0__SpaClientId.',
    )
  }

  return config
}
