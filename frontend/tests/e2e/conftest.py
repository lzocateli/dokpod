import os
import re

import pytest
from playwright.sync_api import Browser, Page, Playwright, expect


BASE_URL = os.environ.get("DOKPOD_E2E_BASE_URL", "https://localhost:7443/dokpod/")
USERNAME = os.environ.get("DOKPOD_E2E_USERNAME")
PASSWORD = os.environ.get("DOKPOD_E2E_PASSWORD")
ENVIRONMENT_ID = os.environ.get("DOKPOD_E2E_ENVIRONMENT_ID")


def require_credentials() -> tuple[str, str]:
    if not USERNAME or not PASSWORD:
        pytest.skip("DOKPOD_E2E_USERNAME e DOKPOD_E2E_PASSWORD não configurados")
    return USERNAME, PASSWORD


@pytest.fixture(scope="session")
def browser(playwright: Playwright) -> Browser:
    return playwright.chromium.launch()


@pytest.fixture
def authenticated_page(browser: Browser) -> Page:
    username, password = require_credentials()
    context = browser.new_context(ignore_https_errors=True)
    page = context.new_page()
    page.goto(BASE_URL, wait_until="domcontentloaded")

    if page.get_by_role("button", name="Entrar com Keycloak").is_visible():
        page.get_by_role("button", name="Entrar com Keycloak").click()

    if "/realms/" in page.url or "protocol/openid-connect" in page.url:
        page.get_by_label("Username").fill(username)
        page.get_by_label("Password", exact=True).fill(password)
        page.get_by_role("button", name="Sign In").click()

    page.wait_for_load_state("networkidle")
    expect(page).not_to_have_url(re.compile(r"/bff/login"))
    yield page
    context.close()