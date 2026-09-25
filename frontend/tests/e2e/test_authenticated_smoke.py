import os

from playwright.sync_api import Page, expect

from conftest import BASE_URL, ENVIRONMENT_ID


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
    expect(authenticated_page.get_by_role("heading", name="Ambientes", exact=True)).to_be_visible()


def test_environment_can_be_opened(authenticated_page: Page) -> None:
    if not ENVIRONMENT_ID:
        import pytest

        pytest.skip("DOKPOD_E2E_ENVIRONMENT_ID não configurado")

    response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/api/v1/environments/{ENVIRONMENT_ID}"
    )
    expect(response).to_be_ok()
    environment = response.json()
    assert environment["environmentId"].lower() == ENVIRONMENT_ID.lower()
    assert {
        "container:start",
        "container:stop",
        "container:restart",
        "container:delete",
    }.issubset(environment["scopes"])

    inventory_response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/api/v1/environments/{ENVIRONMENT_ID}/containers"
    )
    expect(inventory_response).to_be_ok()
    assert inventory_response.json()["containers"]

    authenticated_page.goto(
        f"{BASE_URL.rstrip('/')}/environments/{ENVIRONMENT_ID}/containers",
        wait_until="networkidle",
    )
    expect(authenticated_page.get_by_role("main")).to_be_visible()
    expect(authenticated_page.get_by_text("Containers")).to_be_visible()


def test_lifecycle_view_exposes_authorized_container_actions(
    authenticated_page: Page,
) -> None:
    if not ENVIRONMENT_ID:
        import pytest

        pytest.skip("DOKPOD_E2E_ENVIRONMENT_ID não configurado")

    authenticated_page.goto(
        f"{BASE_URL.rstrip('/')}/environments/{ENVIRONMENT_ID}/containers",
        wait_until="networkidle",
    )
    expect(authenticated_page.get_by_role("main")).to_be_visible()
    expect(authenticated_page.locator(".container-row").first).to_be_visible()
    expect(authenticated_page.get_by_role("button", name="Iniciar", exact=True).first).to_be_visible()
    expect(authenticated_page.get_by_role("button", name="Parar", exact=True).first).to_be_visible()
    expect(authenticated_page.get_by_role("button", name="Reiniciar", exact=True).first).to_be_visible()
    expect(authenticated_page.get_by_role("button", name="Excluir", exact=True).first).to_be_visible()