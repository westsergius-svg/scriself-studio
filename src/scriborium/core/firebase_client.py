"""Firebase Auth + Storage через REST (десктоп, без SDK).

Нужны Web API Key и bucket из консоли Firebase (приложение Web).
Путь объектов в Storage: scriborium/{uid}/{имя_файла} — как в Android.
"""
from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass(slots=True)
class FirebaseAuthTokens:
    id_token: str
    refresh_token: str
    local_id: str
    email: str | None = None


def _read_json_response(resp: Any) -> dict[str, Any]:
    raw = resp.read().decode("utf-8", errors="replace")
    return json.loads(raw) if raw.strip() else {}


def _http_json(url: str, body: dict[str, Any] | None = None, form: bytes | None = None, headers: dict[str, str] | None = None, method: str | None = None) -> dict[str, Any]:
    h = dict(headers or {})
    data: bytes | None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        h.setdefault("Content-Type", "application/json")
    else:
        data = form
        if form is not None:
            h.setdefault("Content-Type", "application/x-www-form-urlencoded")
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:  # noqa: S310 — Firebase HTTPS
            return _read_json_response(resp)
    except urllib.error.HTTPError as e:
        err_body = e.read().decode("utf-8", errors="replace")
        try:
            err_json = json.loads(err_body)
            msg = err_json.get("error", {}).get("message", err_body)
        except json.JSONDecodeError:
            msg = err_body or str(e.reason)
        raise RuntimeError(msg) from e


def firebase_sign_in(api_key: str, email: str, password: str) -> FirebaseAuthTokens:
    url = f"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={urllib.parse.quote(api_key)}"
    data = _http_json(url, {"email": email, "password": password, "returnSecureToken": True})
    return FirebaseAuthTokens(
        id_token=str(data["idToken"]),
        refresh_token=str(data["refreshToken"]),
        local_id=str(data["localId"]),
        email=str(data.get("email") or email),
    )


def firebase_sign_up(api_key: str, email: str, password: str) -> FirebaseAuthTokens:
    url = f"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={urllib.parse.quote(api_key)}"
    data = _http_json(url, {"email": email, "password": password, "returnSecureToken": True})
    return FirebaseAuthTokens(
        id_token=str(data["idToken"]),
        refresh_token=str(data["refreshToken"]),
        local_id=str(data["localId"]),
        email=str(data.get("email") or email),
    )


def firebase_refresh_id_token(api_key: str, refresh_token: str) -> FirebaseAuthTokens:
    url = f"https://securetoken.googleapis.com/v1/token?key={urllib.parse.quote(api_key)}"
    form = urllib.parse.urlencode({"grant_type": "refresh_token", "refresh_token": refresh_token}).encode("utf-8")
    data = _http_json(url, form=form, method="POST")
    return FirebaseAuthTokens(
        id_token=str(data["id_token"]),
        refresh_token=str(data.get("refresh_token") or refresh_token),
        local_id=str(data["user_id"]),
        email=None,
    )


def storage_upload_bytes(
    *,
    api_key: str,
    refresh_token: str,
    storage_bucket: str,
    object_path: str,
    content: bytes,
    content_type: str = "application/octet-stream",
) -> None:
    tokens = firebase_refresh_id_token(api_key, refresh_token)
    encoded_name = urllib.parse.quote(object_path, safe="")
    url = (
        f"https://firebasestorage.googleapis.com/v0/b/{urllib.parse.quote(storage_bucket)}"
        f"/o?uploadType=media&name={encoded_name}"
    )
    req = urllib.request.Request(
        url,
        data=content,
        method="POST",
        headers={
            "Authorization": f"Bearer {tokens.id_token}",
            "Content-Type": content_type,
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=300) as resp:  # noqa: S310
            resp.read()
    except urllib.error.HTTPError as e:
        raise RuntimeError(e.read().decode("utf-8", errors="replace") or str(e.reason)) from e


def storage_list_prefix(
    *,
    api_key: str,
    refresh_token: str,
    storage_bucket: str,
    prefix: str,
) -> list[str]:
    tokens = firebase_refresh_id_token(api_key, refresh_token)
    q = urllib.parse.quote(prefix, safe="")
    url = f"https://firebasestorage.googleapis.com/v0/b/{urllib.parse.quote(storage_bucket)}/o?prefix={q}"
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {tokens.id_token}"})
    with urllib.request.urlopen(req, timeout=120) as resp:  # noqa: S310
        data = _read_json_response(resp)
    items = data.get("items") or []
    names: list[str] = []
    for it in items:
        name = str(it.get("name", ""))
        if name:
            names.append(name)
    return names


def storage_download_bytes(
    *,
    api_key: str,
    refresh_token: str,
    storage_bucket: str,
    object_path: str,
) -> bytes:
    tokens = firebase_refresh_id_token(api_key, refresh_token)
    enc = urllib.parse.quote(object_path, safe="")
    url = f"https://firebasestorage.googleapis.com/v0/b/{urllib.parse.quote(storage_bucket)}/o/{enc}?alt=media"
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {tokens.id_token}"})
    with urllib.request.urlopen(req, timeout=300) as resp:  # noqa: S310
        return resp.read()


def guess_content_type(filename: str) -> str:
    lower = filename.lower()
    if lower.endswith(".scri"):
        return "application/zip"
    if lower.endswith(".txt"):
        return "text/plain; charset=utf-8"
    if lower.endswith(".docx"):
        return "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    return "application/octet-stream"


def user_storage_prefix(uid: str) -> str:
    return f"scriborium/{uid}/"


def upload_local_file(
    *,
    api_key: str,
    refresh_token: str,
    storage_bucket: str,
    uid: str,
    local_path: Path,
    remote_basename: str | None = None,
) -> str:
    name = remote_basename or local_path.name
    object_path = f"{user_storage_prefix(uid)}{name}"
    content = local_path.read_bytes()
    storage_upload_bytes(
        api_key=api_key,
        refresh_token=refresh_token,
        storage_bucket=storage_bucket,
        object_path=object_path,
        content=content,
        content_type=guess_content_type(name),
    )
    return object_path


def download_to_folder(
    *,
    api_key: str,
    refresh_token: str,
    storage_bucket: str,
    object_path: str,
    dest_folder: Path,
) -> Path:
    data = storage_download_bytes(
        api_key=api_key,
        refresh_token=refresh_token,
        storage_bucket=storage_bucket,
        object_path=object_path,
    )
    base = object_path.rsplit("/", maxsplit=1)[-1]
    dest = dest_folder / base
    dest_folder.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(data)
    return dest
