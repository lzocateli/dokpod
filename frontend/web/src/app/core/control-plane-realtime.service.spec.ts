import { describe, expect, it } from 'vitest';
import { controlPlaneHubUrl } from './control-plane-realtime.service';

describe('controlPlaneHubUrl', () => {
  it('preserves the application base path', () => {
    expect(controlPlaneHubUrl('https://localhost:7443/dokpod/')).toBe(
      'https://localhost:7443/dokpod/hubs/control-plane',
    );
  });
});