"""
JSON Serialization and UI Schema export for ELF GUI Designer.
"""

import json
from typing import Dict, Any
from core.models import Project


def generate_json_schema(project: Project) -> str:
    """Exports full project structure as formatted JSON."""
    return project.to_json()


def parse_json_schema(json_str: str) -> Dict[str, Any]:
    """Parses JSON UI schema."""
    return json.loads(json_str)
