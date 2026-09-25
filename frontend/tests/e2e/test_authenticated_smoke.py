import os
import uuid
from datetime import datetime, timedelta, timezone
from urllib.parse import urlsplit

import pytest
from playwright.sync_api import Page, expect

from conftest import BASE_URL, ENVIRONMENT_ID


MUTATION_CONTAINER_NAME = os.environ.get("DOKPOD_E2E_MUTATION_CONTAINER")
DENIED_ENVIRONMENT_ID = os.environ.get("DOKPOD_E2E_DENIED_ENVIRONMENT_ID")
REVOKE_AGENT = os.environ.get("DOKPOD_E2E_REVOKE_AGENT") == "1"
REPORT_DIRECTORY = os.environ.get("DOKPOD_E2E_REPORT_DIRECTORY", "/app/reports")
BASE_URI = urlsplit(BASE_URL)
ORIGIN = f"{BASE_URI.scheme}://{BASE_URI.netloc}"


def test_authenticated_session_is_available(authenticated_page: Page) -> None:
    response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/bff/session"
    )
    expect(response).to_be_ok()
    payload = response.json()
    assert payload.get("authenticated") is True
    assert payload.get("subject")


def test_logout_invalidates_bff_session(authenticated_page: Page) -> None:
    antiforgery_response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/bff/antiforgery"
    )
    expect(antiforgery_response).to_be_ok()
    response = authenticated_page.request.post(
        f"{BASE_URL.rstrip('/')}/bff/logout",
        headers={
            "Origin": ORIGIN,
            "X-Dokpod-Antiforgery": antiforgery_response.json()["requestToken"],
        },
        max_redirects=0,
    )
    assert response.status == 302
    session = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/bff/session"
    ).json()
    assert session["authenticated"] is False


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
    missing_command = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/api/v1/environments/{ENVIRONMENT_ID}/commands/{uuid.uuid4()}"
    )
    assert missing_command.status == 404

    antiforgery_response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/bff/antiforgery"
    )
    duplicate_registration = authenticated_page.request.post(
        f"{BASE_URL.rstrip('/')}/api/v1/environments",
        headers={
            "Origin": ORIGIN,
            "X-Dokpod-Antiforgery": antiforgery_response.json()["requestToken"],
        },
        data={
            "environmentId": environment["environmentId"],
            "name": environment["name"],
            "host": environment["host"],
            "enabled": environment["enabled"],
            "scopes": environment["scopes"],
        },
    )
    assert duplicate_registration.status == 409

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
    expect(authenticated_page.get_by_text("Tempo real: connected", exact=True)).to_be_visible(
        timeout=15_000
    )
    expect(authenticated_page.get_by_role("button", name="Iniciar", exact=True).first).to_be_visible()
    expect(authenticated_page.get_by_role("button", name="Parar", exact=True).first).to_be_visible()
    expect(authenticated_page.get_by_role("button", name="Reiniciar", exact=True).first).to_be_visible()
    expect(authenticated_page.get_by_role("button", name="Excluir", exact=True).first).to_be_visible()


def test_synthetic_container_lifecycle(authenticated_page: Page) -> None:
    if not ENVIRONMENT_ID or not MUTATION_CONTAINER_NAME:
        pytest.skip("Ambiente e container sintético de mutação não configurados")

    authenticated_page.goto(
        f"{BASE_URL.rstrip('/')}/environments/{ENVIRONMENT_ID}/containers",
        wait_until="networkidle",
    )

    def container_row():
        return authenticated_page.locator("article.container-row").filter(
            has=authenticated_page.get_by_role(
                "heading",
                name=MUTATION_CONTAINER_NAME,
                exact=True,
            )
        )

    def run_action(action: str, expected_state: str) -> None:
        row = container_row()
        expect(row).to_have_count(1)
        with authenticated_page.expect_response(
            lambda response: "/commands" in response.url
            and response.request.method == "POST"
        ) as response_info:
            row.get_by_role("button", name=action, exact=True).click()
        response = response_info.value
        assert "x-dokpod-antiforgery" in response.request.all_headers()
        cookie_names = {cookie["name"] for cookie in authenticated_page.context.cookies()}
        assert "__Host-Dokpod.Antiforgery" in cookie_names
        assert response.status == 202, response.text()
        expect(row.locator(".operation")).to_contain_text("succeeded", timeout=30_000)
        expect(row.get_by_text(expected_state, exact=True)).to_be_visible(timeout=30_000)

    run_action("Iniciar", "Running")
    run_action("Reiniciar", "Running")
    run_action("Parar", "Exited")

    row = container_row()
    row.get_by_role("button", name="Excluir", exact=True).click()
    dialog = authenticated_page.get_by_role("dialog")
    expect(dialog).to_contain_text(MUTATION_CONTAINER_NAME)
    expect(dialog).to_contain_text("Volumes não serão excluídos implicitamente")
    dialog.get_by_role("button", name="Excluir container", exact=True).click()
    expect(container_row()).to_have_count(0, timeout=30_000)


def test_horizontal_access_is_denied(authenticated_page: Page) -> None:
    if not DENIED_ENVIRONMENT_ID:
        pytest.skip("Ambiente sintético negado não configurado")

    environment_url = (
        f"{BASE_URL.rstrip('/')}/api/v1/environments/{DENIED_ENVIRONMENT_ID}"
    )
    assert authenticated_page.request.get(environment_url).status == 403
    assert authenticated_page.request.get(f"{environment_url}/containers").status == 403

    antiforgery_response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/bff/antiforgery"
    )
    expect(antiforgery_response).to_be_ok()
    response = authenticated_page.request.post(
        f"{environment_url}/containers/{'A' * 64}/commands",
        headers={
            "Idempotency-Key": str(uuid.uuid4()),
            "Origin": ORIGIN,
            "X-Dokpod-Antiforgery": antiforgery_response.json()["requestToken"],
        },
        data={
            "action": "start",
            "expectedContainerRevision": "synthetic-revision",
            "deadlineUtc": (
                datetime.now(timezone.utc) + timedelta(minutes=2)
            ).isoformat(),
        },
    )
    assert response.status == 403

    authenticated_page.goto(
        f"{BASE_URL.rstrip('/')}/environments/{DENIED_ENVIRONMENT_ID}/containers",
        wait_until="networkidle",
    )
    expect(authenticated_page.get_by_text("Acesso negado", exact=True)).to_be_visible()
    expect(authenticated_page.locator(".realtime-status")).to_have_attribute(
        "data-state",
        "error",
        timeout=15_000,
    )


def test_agent_identity_can_be_revoked(authenticated_page: Page) -> None:
    if not ENVIRONMENT_ID or not REVOKE_AGENT:
        pytest.skip("Revogação destrutiva do agente não habilitada")

    antiforgery_response = authenticated_page.request.get(
        f"{BASE_URL.rstrip('/')}/bff/antiforgery"
    )
    expect(antiforgery_response).to_be_ok()
    response = authenticated_page.request.post(
        f"{BASE_URL.rstrip('/')}/api/v1/environments/{ENVIRONMENT_ID}/agent-identity/revoke",
        headers={
            "Origin": ORIGIN,
            "X-Dokpod-Antiforgery": antiforgery_response.json()["requestToken"],
        },
    )
    assert response.status == 204, response.text()


def test_inventory_visual_states(authenticated_page: Page) -> None:
    if not ENVIRONMENT_ID or not DENIED_ENVIRONMENT_ID:
        pytest.skip("Ambientes sintéticos para verificação visual não configurados")

    os.makedirs(REPORT_DIRECTORY, exist_ok=True)
    inventory_url = (
        f"{BASE_URL.rstrip('/')}/environments/{ENVIRONMENT_ID}/containers"
    )
    sensitive_fields = [
        authenticated_page.locator(".identity"),
        authenticated_page.locator("dd"),
    ]

    authenticated_page.set_viewport_size({"width": 1440, "height": 900})
    authenticated_page.goto(inventory_url, wait_until="networkidle")
    expect(authenticated_page.locator(".realtime-status")).to_have_attribute(
        "data-state", "connected", timeout=15_000
    )
    authenticated_page.screenshot(
        path=f"{REPORT_DIRECTORY}/inventory-desktop.png",
        full_page=True,
        mask=sensitive_fields,
    )

    authenticated_page.set_viewport_size({"width": 390, "height": 844})
    authenticated_page.screenshot(
        path=f"{REPORT_DIRECTORY}/inventory-mobile.png",
        full_page=True,
        mask=sensitive_fields,
    )

    authenticated_page.goto(
        f"{BASE_URL.rstrip('/')}/environments/{DENIED_ENVIRONMENT_ID}/containers",
        wait_until="networkidle",
    )
    expect(authenticated_page.get_by_text("Acesso negado", exact=True)).to_be_visible()
    authenticated_page.screenshot(
        path=f"{REPORT_DIRECTORY}/inventory-forbidden-mobile.png",
        full_page=True,
    )