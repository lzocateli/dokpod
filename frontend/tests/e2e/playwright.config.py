import os
import re

import pytest
from playwright.sync_api import Browser, Page, Playwright, expect


BASE_URL = os.environ.get("DOKPOD_E2E_BASE_URL", "https://localhost:7443/dokpod/")
USERNAME = os.environ.get("DOKPOD_E2E_USERNAME")
PASSWORD = os.environ.get("DOKPOD_E2E_PASSWORD")
ENVIRONMENT_ID = os.environ.get("DOKPOD_E2E_ENVIRONMENT_ID")


def _require_credentials() -> tuple[str, str]:
    if not USERNAME or not PASSWORD:
        pytest.skip("DOKPOD_E2E_USERNAME e DOKPOD_E2E_PASSWORD não configurados")
    return USERNAME, PASSWORD


@pytest.fixture(scope="session")
def browser(playwright: Playwright) -> Browser:
    return playwright.chromium.launch()


@pytest.fixture
def authenticated_page(browser: Browser) -> Page:
    username, password = _require_credentials()
    context = browser.new_context(ignore_https_errors=True)
    page = context.new_page()
    page.goto(BASE_URL, wait_until="domcontentloaded")

    if "/realms/" in page.url or "protocol/openid-connect" in page.url:
        page.get_by_label("Username").fill(username)
        page.get_by_label("Password").fill(password)
        page.get_by_role("button", name="Sign In").click()

    page.wait_for_load_state("networkidle")
    expect(page).not_to_have_url(re.compile(r"/bff/login"))
    yield page
    context.close()


def test_authenticated_session_is_available(authenticated_page: Page) -> None:
    response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/bff/session"
    )
    expect(response).to_be_ok()
    payload = response.json()
    assert payload.get("authenticated") is True
    assert payload.get("subject")


def test_authenticated_catalog_is_rendered(authenticated_page: Page) -> None:
    authenticated_page.goto(BASE_URL, wait_until="networkidle")
    expect(authenticated_page.get_by_role("main")).to_be_visible()
    expect(authenticated_page.get_by_text("Ambientes")).to_be_visible()


def test_environment_can_be_opened(authenticated_page: Page) -> None:
    if not ENVIRONMENT_ID:
        pytest.skip("DOKPOD_E2E_ENVIRONMENT_ID não configurado")

    response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/api/v1/environments/{ENVIRONMENT_ID}"
    )
    expect(response).to_be_ok()
    environment = response.json()
    assert environment["environmentId"].lower() == ENVIRONMENT_ID.lower()

    authenticated_page.goto(
        f"{BASE_URL.rstrip('/')}/environments/{ENVIRONMENT_ID}",
        wait_until="networkidle",
    )
    expect(authenticated_page.get_by_role("main")).to_be_visible()
    expect(authenticated_page.get_by_text("Containers")).to_be_visible()


def test_lifecycle_view_exposes_authorized_container_actions(
    authenticated_page: Page,
) -> None:
    if not ENVIRONMENT_ID:
        pytest.skip("DOKPOD_E2E_ENVIRONMENT_ID não configurado")

    authenticated_page.goto(
        f"{BASE_URL.rstrip('/')}/environments/{ENVIRONMENT_ID}",
        wait_until="networkidle",
    )
    expect(authenticated_page.get_by_role("main")).to_be_visible()
    expect(authenticated_page.get_by_text("Iniciar")).to_be_visible()
    expect(authenticated_page.get_by_text("Parar")).to_be_visible()
    expect(authenticated_page.get_by_text("Reiniciar")).to_be_visible()
    expect(authenticated_page.get_by_text("Excluir")).to_be_visible()
