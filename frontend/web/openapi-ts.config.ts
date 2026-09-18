import { defineConfig } from '@hey-api/openapi-ts';

export default defineConfig({
  input: '../../contracts/openapi/dokpod-control-plane.v1.yaml',
  output: 'src/app/data-access/generated/control-plane',
});
